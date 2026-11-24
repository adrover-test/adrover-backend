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
using Adrover.Application.Services;
using Adrover.Data.Context;
using Adrover.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

RunDailyPlayAllocatorChecks();
RunCampaignSetupChecks();
await WorkerRegressionChecks.RunAsync();

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
        await db.Database.ExecuteSqlRawAsync("DROP TABLE [AdDailyDeliveries], [AdScreenAllocations], [ScheduleRuns], [ContentOutbox]");
        await db.Database.ExecuteSqlRawAsync("DECLARE @sql nvarchar(max) = N''; SELECT @sql += N'ALTER TABLE [Advertisements] DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';' FROM sys.default_constraints dc JOIN sys.columns c ON c.default_object_id = dc.object_id WHERE dc.parent_object_id = OBJECT_ID('dbo.Advertisements') AND c.name IN ('ContentState', 'ReadyForScheduling'); EXEC sp_executesql @sql;");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Advertisements] DROP COLUMN [ContentId], [ContentAssetVersion], [ContentState], [ReadyForScheduling], [ReadyForSchedulingAtUtc], [ContentSubmittedAtUtc], [ContentError], [FirstPlayedAt], [FirstPlaybackDate], [LastReportedDay]");
        var schedulingMigration = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ad-scheduling.sql"));
        await db.Database.ExecuteSqlRawAsync(schedulingMigration);
        await db.Database.ExecuteSqlRawAsync(schedulingMigration);
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
    var paymentRecordId = orderBodies[0].GetProperty("id").GetGuid();
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var saved = await scope.ServiceProvider.GetRequiredService<AdRoverDbContext>().Advertisements.SingleAsync(a => a.Id == id);
        Check(saved.StartDate == new DateTime(2027, 1, 1) && saved.EndDate == new DateTime(2027, 1, 21),
            "A 21-day campaign must include the first and last dates exactly once.");
    }
    Check(paymentRecordId != Guid.Empty && gateway.OrderCount == 1 && orderBodies.All(o =>
        o.GetProperty("orderId").GetString() == "order_test_1" && o.GetProperty("id").GetGuid() == paymentRecordId &&
        o.GetProperty("amountInPaise").GetInt32() == 1575000), "Concurrent checkout must reuse one order with consistent internal ID and paise amount");
    await ExpectStatus(await resumed.PutAsJsonAsync($"/api/Advertisements/drafts/{id}", progress), HttpStatusCode.UnprocessableEntity);
    await ExpectStatus(await Upload(resumed, id, "video", "replacement.mp4"), HttpStatusCode.BadRequest);
    await ExpectStatus(await resumed.PostAsJsonAsync("/api/Payments/verify", new { orderId = "order_test_1", paymentId = "pay_test", signature = "invalid" }), HttpStatusCode.BadRequest);
    const string statusUrl = "/api/Payments/orders/order_test_1/status";
    await ExpectStatus(await anonymous.GetAsync(statusUrl), HttpStatusCode.Forbidden);
    await ExpectStatus(await anonymous.GetAsync($"/api/Payments/{paymentRecordId}"), HttpStatusCode.Forbidden);
    gateway.Payments["order_test_1"] = new RazorpayPaymentDetails("pay_test", "order_test_1", 1575000, "INR", "failed", "upi", null, null);
    var failed = await Data<JsonElement>(await resumed.GetAsync(statusUrl));
    Check(failed.GetProperty("status").GetString() == "Failed", "The status endpoint must report failed attempts");
    var retry = await Data<JsonElement>(await resumed.PostAsJsonAsync("/api/Payments/create-order", new { advertisementId = id, amount = 15750 }));
    Check(retry.GetProperty("status").GetString() == "Pending" && gateway.OrderCount == 1, "Retry must reopen the same order after a failed attempt");
    gateway.Payments["order_test_1"] = gateway.Payments["order_test_1"] with { Status = "authorized" };
    var statusChecks = await Task.WhenAll(resumed.GetAsync(statusUrl), resumed.GetAsync(statusUrl));
    var completed = await Task.WhenAll(statusChecks.Select(Data<JsonElement>));
    Check(completed.All(p => p.GetProperty("status").GetString() == "Completed") && gateway.OrderCount == 1 && gateway.CaptureCount == 1,
        "Concurrent recovery must capture the authorized payment exactly once and reuse the order");
    restored = await Draft(resumed, id);
    Check(restored.PaidAmount == 15750 && restored.Status == "PendingApproval", "Verified payment must persist and submit draft for review");
    await ExpectStatus(await resumed.PostAsJsonAsync("/api/Payments/verify", new { orderId = "order_test_1", paymentId = "pay_test", signature = "valid" }), HttpStatusCode.OK);
    await ExpectStatus(await resumed.PostAsJsonAsync("/api/Payments/verify", new { orderId = "order_test_1", paymentId = "pay_test", signature = "invalid" }), HttpStatusCode.BadRequest);
    var webhook = new { @event = "payment.authorized", payload = new { payment = new { entity = new { id = "pay_test", order_id = "order_test_1", amount = 1575000, currency = "INR", method = "upi" } } } };
    using var webhookRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments/webhook") { Content = JsonContent.Create(webhook) };
    webhookRequest.Headers.Add("X-Razorpay-Signature", "valid");
    await ExpectStatus(await client.SendAsync(webhookRequest), HttpStatusCode.OK);
    Check((await Draft(resumed, id)).PaidAmount == 15750, "Late webhook must not downgrade completed payment");
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AdRoverDbContext>();
        var queued = await db.ContentOutbox.Where(item => item.AdvertisementId == id).ToListAsync();
        var ad = await db.Advertisements.SingleAsync(item => item.Id == id);
        Check(queued.Count == 1 && queued[0].AssetVersion == 1 && queued[0].PaymentId == paymentRecordId &&
              queued[0].VideoBlobPath.StartsWith($"ads/{id}/video_") && ad.ContentState == "Queued",
            "Video-before-payment and repeated payment notifications must persist one private-path outbox message.");
    }

    await ExpectStatus(await resumed.PostAsJsonAsync("/api/Advertisements", new CreateAdvertisementRequest()), HttpStatusCode.BadRequest);
    var created = await Data<AdvertisementDto>(await resumed.PostAsJsonAsync("/api/Advertisements", new CreateAdvertisementRequest
    {
        Name = "Client dashboard campaign", Brand = "Real account", Description = "Dashboard integration test", TargetArea = "Hyderabad",
        VideoDurationSeconds = 12.5m, StartDate = DateTime.UtcNow.Date.AddDays(1), EndDate = DateTime.UtcNow.Date.AddDays(8),
        TargetPlays = 10000, AssignedCabs = 10, ApprovalAmount = 3000
    }));
    await ExpectStatus(await Upload(resumed, created.Id, "video", "campaign.webm"), HttpStatusCode.BadRequest);
    await ExpectStatus(await Upload(resumed, created.Id, "video", "campaign.mp4"), HttpStatusCode.OK);
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
    var paidFirst = await Data<AdvertisementDto>(await resumed.PostAsJsonAsync("/api/Advertisements", new CreateAdvertisementRequest
    {
        Name = "Paid first", Brand = "Real account", Description = "Outbox test", TargetArea = "Hyderabad",
        VideoDurationSeconds = 15, StartDate = DateTime.UtcNow.Date.AddDays(1), EndDate = DateTime.UtcNow.Date.AddDays(8),
        TargetPlays = 10000, AssignedCabs = 10, ApprovalAmount = 3000
    }));
    var paidFirstOrder = await Data<JsonElement>(await resumed.PostAsJsonAsync("/api/Payments/create-order", new { advertisementId = paidFirst.Id, amount = 3000 }));
    var paidFirstOrderId = paidFirstOrder.GetProperty("orderId").GetString()!;
    gateway.Payments[paidFirstOrderId] = new RazorpayPaymentDetails("pay_second", paidFirstOrderId, 300000, "INR", "captured", "upi", null, null);
    await ExpectStatus(await resumed.GetAsync($"/api/Payments/orders/{paidFirstOrderId}/status"), HttpStatusCode.OK);
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AdRoverDbContext>();
        Check(!await db.ContentOutbox.AnyAsync(item => item.AdvertisementId == paidFirst.Id), "Payment alone must not queue content processing.");
    }
    await ExpectStatus(await Upload(resumed, paidFirst.Id, "video", "paid-first.mp4"), HttpStatusCode.OK);
    await ExpectStatus(await Upload(resumed, paidFirst.Id, "video", "replacement.mp4"), HttpStatusCode.BadRequest);
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AdRoverDbContext>();
        var queued = await db.ContentOutbox.Where(item => item.AdvertisementId == paidFirst.Id).ToListAsync();
        var ad = await db.Advertisements.SingleAsync(item => item.Id == paidFirst.Id);
        Check(queued.Count == 1 && queued[0].PaymentId == paidFirstOrder.GetProperty("id").GetGuid() && ad.ContentState == "Queued",
            "Payment-before-video must queue exactly once and reject replacing the queued video.");
    }
    var restrictionAd = await Data<AdvertisementDto>(await resumed.PostAsJsonAsync("/api/Advertisements", new CreateAdvertisementRequest
    {
        Name = "Restrictions campaign", Brand = "Real account", Description = "Restriction test", TargetArea = "Hyderabad",
        VideoDurationSeconds = 12.5m, StartDate = DateTime.UtcNow.Date.AddDays(1), EndDate = DateTime.UtcNow.Date.AddDays(8),
        TargetPlays = 10000, AssignedCabs = 10, ApprovalAmount = 3000
    }));
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AdRoverDbContext>();
        var ad = await db.Advertisements.SingleAsync(a => a.Id == restrictionAd.Id);
        ad.StartDate = DateTime.UtcNow.AddHours(5.5).Date.AddDays(-1);
        await db.SaveChangesAsync();
    }
    var incompleteAd = await Data<AdvertisementDto>(await resumed.GetAsync($"/api/Advertisements/{restrictionAd.Id}"));
    Check(incompleteAd.SetupValidationMessage == CampaignSetup.StartDatePassedMessage,
        "The advertisement API must expose the exact incomplete-setup message.");
    var nextDay = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5).Date).AddDays(1);
    await ExpectStatus(await anonymous.PutAsJsonAsync($"/api/Advertisements/{restrictionAd.Id}/reschedule", new { startDate = nextDay }), HttpStatusCode.NotFound);
    await ExpectStatus(await resumed.PutAsJsonAsync($"/api/Advertisements/{restrictionAd.Id}/reschedule", new { startDate = nextDay.AddDays(-1) }), HttpStatusCode.UnprocessableEntity);
    var allocationId = Guid.NewGuid();
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AdRoverDbContext>();
        var run = new Adrover.Domain.Entities.ScheduleRun { RunDate = nextDay, StartedAtUtc = DateTime.UtcNow };
        db.ScheduleRuns.Add(run);
        await db.SaveChangesAsync();
        db.AdScreenAllocations.Add(new Adrover.Domain.Entities.AdScreenAllocation
        {
            Id = allocationId, AdvertisementId = restrictionAd.Id, RunId = run.Id, ScreenId = "screen",
            ContentId = "creative", PlayDate = nextDay, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 0),
            Status = "Unconfirmed", CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
    foreach (var dispatchState in new[] { "Submitting", "Sent", "Unconfirmed" })
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AdRoverDbContext>();
            var allocation = await db.AdScreenAllocations.SingleAsync(item => item.Id == allocationId);
            allocation.Status = dispatchState;
            await db.SaveChangesAsync();
        }
        await ExpectStatus(await resumed.PutAsJsonAsync($"/api/Advertisements/{restrictionAd.Id}/reschedule", new { startDate = nextDay }), HttpStatusCode.UnprocessableEntity);
    }
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AdRoverDbContext>();
        var allocation = await db.AdScreenAllocations.SingleAsync(item => item.Id == allocationId);
        allocation.Status = "Failed";
        await db.SaveChangesAsync();
    }
    var rescheduled = await Data<AdvertisementDto>(await resumed.PutAsJsonAsync($"/api/Advertisements/{restrictionAd.Id}/reschedule", new { startDate = nextDay }));
    Check(DateOnly.FromDateTime(rescheduled.StartDate) == nextDay &&
          (rescheduled.EndDate.Date - rescheduled.StartDate.Date).Days == (incompleteAd.EndDate.Date - incompleteAd.StartDate.Date).Days &&
          rescheduled.TargetPlays == incompleteAd.TargetPlays && rescheduled.SetupValidationMessage == null,
        "Rescheduling must preserve the purchased duration and contracted plays and clear the overdue message.");
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AdRoverDbContext>();
        var ad = await db.Advertisements.SingleAsync(a => a.Id == restrictionAd.Id);
        ad.FirstPlaybackDate = nextDay;
        ad.LastReportedDay = nextDay;
        await db.SaveChangesAsync();
    }
    await ExpectStatus(await Upload(resumed, restrictionAd.Id, "video", "replacement.mp4"), HttpStatusCode.UnprocessableEntity);
    await ExpectStatus(await Upload(resumed, restrictionAd.Id, "image", "replacement.png"), HttpStatusCode.UnprocessableEntity);
    await ExpectStatus(await resumed.PutAsJsonAsync($"/api/Advertisements/{restrictionAd.Id}/reschedule", new { startDate = nextDay.AddDays(1) }), HttpStatusCode.UnprocessableEntity);
    await ExpectStatus(await resumed.DeleteAsync($"/api/Advertisements/{restrictionAd.Id}"), HttpStatusCode.UnprocessableEntity);
    await ExpectStatus(await resumed.PutAsJsonAsync($"/api/Advertisements/{restrictionAd.Id}", new UpdateAdvertisementRequest
    {
        Name = "Changed", Brand = "Real account", Description = "Changed", TargetArea = "Hyderabad",
        VideoDurationSeconds = 12.5m, StartDate = DateTime.UtcNow.Date.AddDays(1), EndDate = DateTime.UtcNow.Date.AddDays(8),
        TargetPlays = 10000, AssignedCabs = 10, ApprovalAmount = 3000
    }), HttpStatusCode.UnprocessableEntity);
    var startedAd = await Data<AdvertisementDto>(await resumed.GetAsync($"/api/Advertisements/{restrictionAd.Id}"));
    Check(startedAd.FirstPlayedAt == null && startedAd.FirstPlaybackDate == nextDay && startedAd.LastReportedDay == nextDay &&
          !startedAd.IsEditable && startedAd.Name == "Restrictions campaign",
        "A date-only report must lock edits without fabricating a timestamp or changing the campaign.");
    Console.WriteLine("PASS: registration, uploads, payments, campaign lifecycle, playback edit locks, dashboard, previews and private billing history.");
}
finally
{
    await app.StopAsync();
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AdRoverDbContext>().Database.EnsureDeletedAsync();
}

static void RunDailyPlayAllocatorChecks()
{
    Check(DailyPlayAllocator.CalculateCommercialSecondsPerScreen() == 28_800,
        "The default 10-hour window must reserve 20 percent of each 180-second loop.");

    var start = new DateOnly(2026, 11, 24);
    var end = new DateOnly(2026, 12, 23);
    var campaignId = Guid.NewGuid();
    const int playDurationSeconds = 15;
    var screens = Enumerable.Range(1, 5)
        .Select(index => new ScreenCommercialCapacity($"screen-{index}", 100 * playDurationSeconds))
        .ToArray();

    var firstDay = DailyPlayAllocator.Allocate(start,
        [new CampaignPlayDemand(campaignId, start, end, 10_000, 0, playDurationSeconds)], screens);
    Check(firstDay.Campaigns.Single().DailyTarget == 334 && firstDay.Campaigns.Single().ScheduledPlays == 334,
        "The campaign start date must be included in the contracted-plays division.");
    Check(firstDay.ScreenAllocations.Sum(item => item.PlannedPlays) == 334 &&
          firstDay.HouseAdAllocations.Sum(item => item.AvailableSeconds) == 166 * playDurationSeconds,
        "Per-screen capacity must be shared fairly and remaining inventory must go to the house ad.");

    var nextDay = DailyPlayAllocator.Allocate(start.AddDays(1),
        [new CampaignPlayDemand(campaignId, start, end, 10_000, 300, playDurationSeconds)], screens);
    Check(nextDay.Campaigns.Single().DailyTarget == 335,
        "Under-delivery on the first day must be redistributed across remaining campaign days.");
    var lastDay = DailyPlayAllocator.Allocate(end,
        [new CampaignPlayDemand(campaignId, start, end, 10_000, 9_750, playDurationSeconds)], screens);
    Check(lastDay.Campaigns.Single().DailyTarget == 250,
        "The last campaign day must target every remaining contracted play.");
    var oneDay = DailyPlayAllocator.Allocate(start,
        [new CampaignPlayDemand(campaignId, start, start, 100, 0, playDurationSeconds)], screens);
    Check(oneDay.Campaigns.Single().DailyTarget == 100,
        "A one-day campaign must allocate its full contract on its start date.");

    var beforeStart = DailyPlayAllocator.Allocate(start.AddDays(-1),
        [new CampaignPlayDemand(campaignId, start, end, 10_000, 0, playDurationSeconds)], screens);
    Check(beforeStart.Campaigns.Single().DailyTarget == 0 &&
          beforeStart.HouseAdAllocations.Sum(item => item.AvailableSeconds) == 500 * playDurationSeconds,
        "Campaign plays must not be allocated before the start date; available inventory goes to the house ad.");

    var completed = DailyPlayAllocator.Allocate(start.AddDays(1),
        [new CampaignPlayDemand(campaignId, start, end, 10_000, 10_000, playDurationSeconds)], screens);
    Check(completed.Campaigns.Single().DailyTarget == 0 &&
          completed.HouseAdAllocations.Sum(item => item.AvailableSeconds) == 500 * playDurationSeconds,
        "A fulfilled campaign must stop receiving plays and release its inventory to the house ad.");

    var constrained = DailyPlayAllocator.Allocate(start,
        [
            new CampaignPlayDemand(Guid.NewGuid(), start, end, 300, 0, playDurationSeconds),
            new CampaignPlayDemand(Guid.NewGuid(), start, end, 300, 0, playDurationSeconds)
        ],
        [new ScreenCommercialCapacity("limited-screen", 20 * playDurationSeconds)]);
    Check(constrained.Campaigns.Sum(item => item.ScheduledPlays) == 20 &&
          constrained.Campaigns.All(item => item.ScheduledPlays == 10),
        "When commercial capacity is limited, active campaigns must share it proportionally.");

    var proportional = DailyPlayAllocator.Allocate(start,
        [new CampaignPlayDemand(Guid.NewGuid(), start, end, 600, 0, 10)],
        [new ScreenCommercialCapacity("small", 100), new ScreenCommercialCapacity("large", 300)]);
    Check(proportional.ScreenAllocations.Single(item => item.ScreenId == "small").PlannedPlays == 5 &&
          proportional.ScreenAllocations.Single(item => item.ScreenId == "large").PlannedPlays == 15,
        "A screen with three times the capacity must receive three times as many plays.");

    var shortAd = Guid.NewGuid();
    var longAd = Guid.NewGuid();
    var fragmented = DailyPlayAllocator.Allocate(start,
        [
            new CampaignPlayDemand(shortAd, start, end, 300, 0, 5),
            new CampaignPlayDemand(longAd, start, end, 30, 0, 1_000)
        ],
        [new ScreenCommercialCapacity("small-capacity", 100)]);
    Check(fragmented.Campaigns.Single(item => item.AdvertisementId == shortAd).ScheduledPlays == 10 &&
          fragmented.Campaigns.Single(item => item.AdvertisementId == longAd).Shortfall == 1 &&
          fragmented.HouseAdAllocations.Single().AvailableSeconds == 50,
        "An ad too long to fit must not strand capacity needed by another contracted campaign.");

    var durationWeighted = DailyPlayAllocator.Allocate(start,
        [
            new CampaignPlayDemand(Guid.NewGuid(), start, end, 300, 0, 10),
            new CampaignPlayDemand(Guid.NewGuid(), start, end, 300, 0, 20)
        ],
        [new ScreenCommercialCapacity("duration-screen", 150)]);
    Check(durationWeighted.Campaigns.All(item => item.ScheduledPlays == 5) &&
          durationWeighted.ScreenAllocations.Sum(item => item.PlannedPlays *
              (item.AdvertisementId == durationWeighted.Campaigns[0].AdvertisementId ? 10 : 20)) <= 150,
        $"Different creative durations must share capacity by playback time. Campaigns: {string.Join(",", durationWeighted.Campaigns.Select(item => $"{item.DailyTarget}/{item.ScheduledPlays}"))}; allocations: {string.Join(",", durationWeighted.ScreenAllocations.Select(item => $"{item.PlannedPlays}"))}; house seconds: {durationWeighted.HouseAdAllocations.Sum(item => item.AvailableSeconds)}.");
}

static void RunCampaignSetupChecks()
{
    var ad = new Adrover.Domain.Entities.Advertisement
    {
        StartDate = new DateTime(2026, 11, 24),
        EndDate = new DateTime(2026, 12, 1)
    };
    var beforeMidnightInIndia = new DateTimeOffset(2026, 11, 24, 18, 29, 0, TimeSpan.Zero);
    var afterMidnightInIndia = beforeMidnightInIndia.AddMinutes(2);
    Check(CampaignSetup.GetValidationMessage(ad, beforeMidnightInIndia) == null,
        "The campaign start date has not passed until midnight in India.");
    Check(CampaignSetup.GetValidationMessage(ad, afterMidnightInIndia) == CampaignSetup.StartDatePassedMessage,
        "Incomplete setup must show the exact overdue message after the start date in India.");
    ad.ContentId = "creative-id";
    ad.ReadyForScheduling = true;
    ad.ApprovalAmount = 100;
    ad.PaidAmount = 100;
    ad.ReadyForSchedulingAtUtc = new DateTime(2026, 11, 24, 18, 31, 0, DateTimeKind.Utc);
    Check(CampaignSetup.GetValidationMessage(ad, afterMidnightInIndia) == CampaignSetup.StartDatePassedMessage,
        "Completing setup after the start date must not retroactively clear the rescheduling requirement.");
    ad.ReadyForSchedulingAtUtc = new DateTime(2026, 11, 24, 18, 29, 0, DateTimeKind.Utc);
    Check(CampaignSetup.GetValidationMessage(ad, afterMidnightInIndia) == null,
        "A campaign ready on its start date must not be marked incomplete later.");
    ad.Status = Adrover.Domain.Entities.AdvertisementStatus.Completed;
    ad.ActualPlays = 1;
    Check(CampaignSetup.HasStarted(ad),
        "A completed campaign with recorded plays stays locked even when the exact first-play time is unavailable.");
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
    private int _captureCount;
    public int OrderCount => _orderCount;
    public int CaptureCount => _captureCount;
    public string KeyId => "rzp_test_fixture";
    public ConcurrentDictionary<string, RazorpayPaymentDetails> Payments { get; } = new();
    public Task<RazorpayOrder> CreateOrderAsync(decimal amount, string currency, string receipt, Dictionary<string, string> notes) =>
        Task.FromResult(new RazorpayOrder($"order_test_{Interlocked.Increment(ref _orderCount)}", decimal.ToInt32(amount * 100), currency, "created"));
    public bool VerifyPaymentSignature(string orderId, string paymentId, string signature) => signature == "valid";
    public bool VerifyWebhookSignature(string payload, string signature) => signature == "valid";
    public Task<RazorpayPaymentDetails> GetPaymentDetailsAsync(string paymentId) => Task.FromResult(Payments.Values.Single(p => p.Id == paymentId));
    public Task<IReadOnlyList<RazorpayPaymentDetails>> GetOrderPaymentsAsync(string orderId) =>
        Task.FromResult<IReadOnlyList<RazorpayPaymentDetails>>(Payments.TryGetValue(orderId, out var payment) ? [payment] : []);
    public async Task<RazorpayPaymentDetails> CapturePaymentAsync(string paymentId, int amount, string currency)
    {
        Interlocked.Increment(ref _captureCount);
        var payment = await GetPaymentDetailsAsync(paymentId);
        CheckCapture(payment, amount, currency);
        var captured = payment with { Status = "captured" };
        if (!Payments.TryUpdate(payment.OrderId, captured, payment))
            throw new InvalidOperationException("Concurrent capture detected");
        return captured;
    }

    private static void CheckCapture(RazorpayPaymentDetails payment, int amount, string currency)
    {
        if (payment.Status != "authorized" || payment.Amount != amount || payment.Currency != currency)
            throw new InvalidOperationException("Only the full authorized amount may be captured");
    }
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
