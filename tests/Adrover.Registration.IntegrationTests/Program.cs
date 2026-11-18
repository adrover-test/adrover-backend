using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Adrover.Api.Configuration;
using Adrover.Api.Controllers;
using Adrover.Api.Middleware;
using Adrover.Application.DTOs.Advertisement;
using Adrover.Data.Context;
using Adrover.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

var databaseName = $"Adrover_RegistrationTests_{Guid.NewGuid():N}";
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ConnectionStrings:DefaultConnection"] = $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true",
    ["Jwt:SecretKey"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
    ["Jwt:Issuer"] = "registration-tests",
    ["Jwt:Audience"] = "registration-tests"
});
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
var gateway = new TestGateway();
var storage = new TestStorage();
builder.Services.Replace(ServiceDescriptor.Singleton<IPaymentGatewayService>(gateway));
builder.Services.Replace(ServiceDescriptor.Singleton<IFileStorageService>(storage));
await using var app = builder.Build();
app.UseExceptionHandling();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Urls.Add("http://127.0.0.1:0");
try
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AdRoverDbContext>();
        await db.Database.EnsureCreatedAsync();
        // Exercise the additive migration against a pre-upgrade schema, twice.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Advertisements] DROP COLUMN [RegistrationProgress]");
        var migration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "registration-drafts.sql"));
        await db.Database.ExecuteSqlRawAsync(migration);
        await db.Database.ExecuteSqlRawAsync(migration);
    }
    await app.StartAsync();
    using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    var id = Guid.NewGuid();
    var progress = new RegistrationDraftRequest
    {
        Id = id, Name = "Resumable campaign", Brand = "Registration test", TargetArea = "Hyderabad",
        VideoDurationSeconds = 12.5m, Days = "7", DailyPlays = "1000", Cabs = "10"
    };
    await ExpectStatus(await client.PostAsJsonAsync("/api/v1/auth/register", new
    {
        email = "owner@example.test", phone = "919000000001", fullName = "Test Owner", password = "StrongPass1!", role = "Client", advertisement = progress
    }), HttpStatusCode.OK);
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AdRoverDbContext>();
        Check(await db.Advertisements.AnyAsync(a => a.Id == id && a.Status == Adrover.Domain.Entities.AdvertisementStatus.Draft), "Draft must exist before login or uploads");
    }
    await Login(client, "owner@example.test");
    var initial = await Draft(client, id);
    Check(!initial.HasVideo && !initial.HasBanner, "Interrupted uploads must be resumable");
    var list = await Data<List<RegistrationDraftDto>>(await client.GetAsync("/api/Advertisements/drafts"));
    Check(list.Count == 1 && list[0].Id == id, "Draft must appear after login");
    using var anonymous = new HttpClient { BaseAddress = client.BaseAddress };
    await ExpectStatus(await anonymous.GetAsync($"/api/Advertisements/{id}"), HttpStatusCode.NotFound);
    await ExpectStatus(await anonymous.GetAsync($"/api/Advertisements/drafts/{id}"), HttpStatusCode.Unauthorized);
    await ExpectStatus(await anonymous.PostAsJsonAsync("/api/v1/auth/register", new
    {
        email = "other@example.test", phone = "919000000002", fullName = "Other Owner", password = "StrongPass1!", role = "Client"
    }), HttpStatusCode.OK);
    await Login(anonymous, "other@example.test");
    await ExpectStatus(await anonymous.GetAsync($"/api/Advertisements/drafts/{id}"), HttpStatusCode.NotFound);
    await ExpectStatus(await anonymous.PutAsJsonAsync($"/api/Advertisements/drafts/{id}", progress), HttpStatusCode.NotFound);
    await ExpectStatus(await Upload(anonymous, id, "video", "clip.mp4"), HttpStatusCode.Forbidden);

    storage.FailNextUpload = true;
    await ExpectStatus(await Upload(client, id, "video", "clip.mp4"), HttpStatusCode.InternalServerError);
    Check(!(await Draft(client, id)).HasVideo, "Failed upload must not mark asset saved");
    await ExpectStatus(await Upload(client, id, "video", "clip.mp4"), HttpStatusCode.OK);
    await ExpectStatus(await Upload(client, id, "image", "banner.png"), HttpStatusCode.OK);
    progress.Days = "21";
    progress.DailyPlays = "2500";
    progress.Cabs = "30";
    progress.StartDate = "2027-01-01";
    progress.Consent = true;
    progress.Step = "payment";
    await ExpectStatus(await client.PutAsJsonAsync($"/api/Advertisements/drafts/{id}", progress), HttpStatusCode.OK);
    using var resumed = new HttpClient { BaseAddress = client.BaseAddress };
    await Login(resumed, "owner@example.test");
    var restored = await Draft(resumed, id);
    Check(restored.HasVideo && restored.HasBanner && restored.Progress.Days == "21" && restored.Progress.Step == "payment", "Fresh session must restore plan, step and uploaded assets");
    await ExpectStatus(await resumed.PostAsJsonAsync("/api/Payments/create-order", new { advertisementId = id, amount = 1 }), HttpStatusCode.BadRequest);
    Check(gateway.OrderCount == 0, "Incorrect amount must not create an order");
    var orders = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => resumed.PostAsJsonAsync("/api/Payments/create-order", new { advertisementId = id, amount = 15750 })));
    var orderBodies = await Task.WhenAll(orders.Select(Data<JsonElement>));
    Check(gateway.OrderCount == 1 && orderBodies.All(o => o.GetProperty("orderId").GetString() == "order_test_1"), "Concurrent checkout must reuse one order");
    await ExpectStatus(await resumed.PutAsJsonAsync($"/api/Advertisements/drafts/{id}", progress), HttpStatusCode.UnprocessableEntity);
    await ExpectStatus(await Upload(resumed, id, "video", "replacement.mp4"), HttpStatusCode.BadRequest);
    await ExpectStatus(await resumed.PostAsJsonAsync("/api/Payments/verify", new { orderId = "order_test_1", paymentId = "pay_test", signature = "invalid" }), HttpStatusCode.BadRequest);
    gateway.Payments["order_test_1"] = new RazorpayPaymentDetails("pay_test", "order_test_1", 1575000, "INR", "authorized", "upi", null, null);
    var processing = await Data<JsonElement>(await resumed.PostAsJsonAsync("/api/Payments/create-order", new { advertisementId = id, amount = 15750 }));
    Check(processing.GetProperty("status").GetString() == "Processing", "Authorized payments must block a second checkout");
    gateway.Payments["order_test_1"] = gateway.Payments["order_test_1"] with { Status = "captured" };
    var completed = await Data<JsonElement>(await resumed.PostAsJsonAsync("/api/Payments/create-order", new { advertisementId = id, amount = 15750 }));
    Check(completed.GetProperty("status").GetString() == "Completed" && gateway.OrderCount == 1, "Disconnect recovery must recognize capture without another charge");
    restored = await Draft(resumed, id);
    Check(restored.PaidAmount == 15750 && restored.Status == "PendingApproval", "Verified payment must persist and submit draft for review");
    await ExpectStatus(await resumed.PostAsJsonAsync("/api/Payments/verify", new { orderId = "order_test_1", paymentId = "pay_test", signature = "valid" }), HttpStatusCode.OK);
    var webhook = new { @event = "payment.authorized", payload = new { payment = new { entity = new { id = "pay_test", order_id = "order_test_1", amount = 1575000, currency = "INR", method = "upi" } } } };
    using var webhookRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments/webhook") { Content = JsonContent.Create(webhook) };
    webhookRequest.Headers.Add("X-Razorpay-Signature", "valid");
    await ExpectStatus(await client.SendAsync(webhookRequest), HttpStatusCode.OK);
    Check((await Draft(resumed, id)).PaidAmount == 15750, "Late webhook must not downgrade completed payment");

    await ExpectStatus(await resumed.PostAsJsonAsync("/api/Advertisements", new CreateAdvertisementRequest()), HttpStatusCode.BadRequest);
    var created = await Data<AdvertisementDto>(await resumed.PostAsJsonAsync("/api/Advertisements", new CreateAdvertisementRequest
    {
        Name = "Client dashboard campaign", Brand = "Real account", Description = "Dashboard integration test", TargetArea = "Hyderabad",
        VideoDurationSeconds = 12.5m, StartDate = DateTime.UtcNow.Date.AddDays(1), EndDate = DateTime.UtcNow.Date.AddDays(8),
        TargetPlays = 10000, AssignedCabs = 10, ApprovalAmount = 3000
    }));
    await ExpectStatus(await Upload(resumed, created.Id, "video", "campaign.webm"), HttpStatusCode.OK);
    await ExpectStatus(await Upload(resumed, created.Id, "image", "banner.webp"), HttpStatusCode.OK);
    var ownAds = await Data<AdvertisementListResponse>(await resumed.GetAsync("/api/Advertisements/my-ads?pageNumber=1&pageSize=100"));
    Check(ownAds.Total == 2 && ownAds.Items.Any(a => a.Id == created.Id && a.VideoUrl != null && a.ImageUrl != null), "Created campaign and both assets must appear in real account list");
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AdRoverDbContext>();
        var ownAd = await db.Advertisements.SingleAsync(a => a.Id == created.Id);
        ownAd.ActualPlays = 2;
        var otherUser = await db.Users.SingleAsync(u => u.Email == "other@example.test");
        var otherAd = new Adrover.Domain.Entities.Advertisement
        {
            Id = Guid.NewGuid(), ClientId = otherUser.Id, Name = "Other client", Brand = "Private", TargetArea = "Other area",
            StartDate = DateTime.UtcNow.Date, EndDate = DateTime.UtcNow.Date.AddDays(7), SubmittedAt = DateTime.UtcNow, ActualPlays = 50
        };
        db.Advertisements.Add(otherAd);
        foreach (var adId in new[] { created.Id, created.Id, otherAd.Id })
            db.AdPlays.Add(new Adrover.Domain.Entities.AdPlay
            {
                Id = Guid.NewGuid(), AdvertisementId = adId, VehicleNumber = "TS01AB1234", DriverPhone = "919000000099",
                Latitude = 17.44, Longitude = 78.35, DurationSeconds = 13, PlayedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
    }
    var summary = await Data<JsonElement>(await resumed.GetAsync("/api/ClientDashboard/summary"));
    Check(summary.GetProperty("totalAdvertisements").GetInt32() == 2 && summary.GetProperty("totalPlays").GetInt64() == 2, "Dashboard must only aggregate this client's persisted campaigns");
    Check(summary.GetProperty("activeCabs").GetInt32() == 1 && summary.GetProperty("dailyPlays").GetArrayLength() == 14 &&
        summary.GetProperty("dailyPlays").EnumerateArray().Sum(day => day.GetProperty("plays").GetInt64()) == 2, "Activity must use recorded plays, zero-fill days and deduplicate vehicles");
    var playback = await Data<JsonElement>(await resumed.GetAsync($"/api/ClientDashboard/campaigns/{created.Id}/playback?pageNumber=1&pageSize=1"));
    Check(playback.GetProperty("total").GetInt32() == 2 && playback.GetProperty("items").GetArrayLength() == 1, "Playback must paginate recorded events");
    Check(!playback.ToString().Contains("919000000099"), "Client history must not expose driver contact details");
    var creative = await Data<JsonElement>(await resumed.GetAsync($"/api/ClientDashboard/campaigns/{created.Id}/creative"));
    Check(creative.GetProperty("videoUrl").GetString()!.Contains("preview-test") && creative.GetProperty("imageUrl").GetString()!.Contains("preview-test"), "Private media must receive temporary preview links");
    await ExpectStatus(await anonymous.GetAsync($"/api/ClientDashboard/campaigns/{created.Id}/creative"), HttpStatusCode.NotFound);
    await ExpectStatus(await anonymous.GetAsync($"/api/ClientDashboard/campaigns/{created.Id}/playback"), HttpStatusCode.NotFound);
    await ExpectStatus(await resumed.GetAsync($"/api/ClientDashboard/campaigns/{created.Id}/playback?pageNumber=0"), HttpStatusCode.UnprocessableEntity);
    var profile = await Data<JsonElement>(await resumed.GetAsync("/api/Users/me"));
    Check(profile.GetProperty("email").GetString() == "owner@example.test", "Profile must belong to the signed-in client");
    await ExpectStatus(await resumed.PutAsJsonAsync("/api/Users/me", new { name = "Updated Client Name" }), HttpStatusCode.OK);
    var savedProfile = await Data<JsonElement>(await resumed.GetAsync("/api/Users/me"));
    Check(savedProfile.GetProperty("name").GetString() == "Updated Client Name" &&
        savedProfile.GetProperty("email").GetString() == "owner@example.test" &&
        savedProfile.GetProperty("phone").GetString() == profile.GetProperty("phone").GetString(), "Profile updates must persist without changing contact details");
    var history = await Data<JsonElement>(await resumed.GetAsync("/api/Payments/history?pageNumber=1&pageSize=1"));
    var paidItem = history.GetProperty("items")[0];
    Check(history.GetProperty("total").GetInt32() == 1 && history.GetProperty("items").GetArrayLength() == 1 &&
        paidItem.GetProperty("advertisementId").GetGuid() == id && paidItem.GetProperty("amount").GetDecimal() == 15750 &&
        paidItem.GetProperty("status").GetString() == "Completed", "Billing must return the client's persisted captured payment");
    var nextPayments = await Data<JsonElement>(await resumed.GetAsync("/api/Payments/history?pageNumber=2&pageSize=1"));
    Check(nextPayments.GetProperty("total").GetInt32() == 1 && nextPayments.GetProperty("items").GetArrayLength() == 0, "Billing must paginate without inventing extra payments");
    var otherPayments = await Data<JsonElement>(await anonymous.GetAsync("/api/Payments/history"));
    Check(otherPayments.GetProperty("total").GetInt32() == 0 && otherPayments.GetProperty("items").GetArrayLength() == 0, "Billing must not expose another client's payments");
    using var unsigned = new HttpClient { BaseAddress = client.BaseAddress };
    await ExpectStatus(await unsigned.GetAsync("/api/ClientDashboard/summary"), HttpStatusCode.Unauthorized);
    await ExpectStatus(await unsigned.GetAsync("/api/Users/me"), HttpStatusCode.Unauthorized);
    await ExpectStatus(await unsigned.GetAsync("/api/Payments/history"), HttpStatusCode.Unauthorized);
    Console.WriteLine("PASS: registration, uploads, payments, real client campaign creation/list, dashboard aggregates, private previews, owner-scoped playback, persisted profiles and paginated private billing history.");
}
finally
{
    await app.StopAsync();
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AdRoverDbContext>().Database.EnsureDeletedAsync();
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static async Task ExpectStatus(HttpResponseMessage response, HttpStatusCode expected)
{
    if (response.StatusCode != expected)
        throw new InvalidOperationException($"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    response.Dispose();
}
static async Task<T> Data<T>(HttpResponseMessage response)
{
    using (response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {response.StatusCode}: {text}");
        using var json = JsonDocument.Parse(text);
        return json.RootElement.GetProperty("data").Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Response has no data");
    }
}
static async Task<RegistrationDraftDto> Draft(HttpClient client, Guid id) =>
    await Data<RegistrationDraftDto>(await client.GetAsync($"/api/Advertisements/drafts/{id}"));
static async Task Login(HttpClient client, string email)
{
    var response = await Data<JsonElement>(await client.PostAsJsonAsync("/api/v1/auth/client-login", new { email, password = "StrongPass1!" }));
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.GetProperty("accessToken").GetString());
}
static async Task<HttpResponseMessage> Upload(HttpClient client, Guid id, string kind, string fileName)
{
    using var content = new MultipartFormDataContent();
    content.Add(new ByteArrayContent([1, 2, 3, 4]), "file", fileName);
    return await client.PostAsync($"/api/Advertisements/{id}/upload-{kind}", content);
}

sealed class TestGateway : IPaymentGatewayService
{
    private int _orderCount;
    public int OrderCount => _orderCount;
    public string KeyId => "rzp_test_fixture";
    public ConcurrentDictionary<string, RazorpayPaymentDetails> Payments { get; } = new();
    public Task<RazorpayOrder> CreateOrderAsync(decimal amount, string currency, string receipt, Dictionary<string, string> notes) =>
        Task.FromResult(new RazorpayOrder($"order_test_{Interlocked.Increment(ref _orderCount)}", decimal.ToInt32(amount * 100), currency, "created"));
    public bool VerifyPaymentSignature(string orderId, string paymentId, string signature) => signature == "valid";
    public bool VerifyWebhookSignature(string payload, string signature) => signature == "valid";
    public Task<RazorpayPaymentDetails> GetPaymentDetailsAsync(string paymentId) => Task.FromResult(Payments.Values.Single(p => p.Id == paymentId));
    public Task<IReadOnlyList<RazorpayPaymentDetails>> GetOrderPaymentsAsync(string orderId) =>
        Task.FromResult<IReadOnlyList<RazorpayPaymentDetails>>(Payments.TryGetValue(orderId, out var payment) ? [payment] : []);
}
sealed class TestStorage : IFileStorageService
{
    public bool FailNextUpload { get; set; }
    public Task<string> UploadFileAsync(string containerName, string fileName, Stream stream)
    {
        if (FailNextUpload) { FailNextUpload = false; throw new IOException("Simulated storage outage"); }
        return Task.FromResult($"https://storage.invalid/{containerName}/{fileName}");
    }
    public Task<bool> DeleteFileAsync(string containerName, string fileName) => throw new NotSupportedException();
    public Task<Stream> DownloadFileAsync(string containerName, string fileName) => throw new NotSupportedException();
    public Task<string> GenerateSasUrlAsync(string containerName, string fileName, TimeSpan expiration)
    {
        if (expiration != TimeSpan.FromMinutes(15)) throw new InvalidOperationException("Preview links must be short-lived.");
        return Task.FromResult($"https://storage.invalid/{containerName}/{fileName}?preview-test=true");
    }
    public Task<IEnumerable<string>> ListFilesAsync(string containerName) => throw new NotSupportedException();
}
