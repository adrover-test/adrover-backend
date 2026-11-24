using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Adrover.Data.Context;
using Adrover.Domain.Entities;
using Adrover.Functions.Configuration;
using Adrover.Functions.Contracts;
using Adrover.Functions.Intelisa;
using Adrover.Functions.Services;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

internal static class WorkerRegressionChecks
{
    public static async Task RunAsync()
    {
        await StaleProofUsesReportedFreshnessAndUnconfirmedDispatchIsPersisted();
        await MissingTargetAreaMappingBlocksPaidCreative();
        await IntelisaUploadRejectionIsClassifiedAsDefinitive();
        await ContentProcessorUsesLatestOutboxVersion();
        Console.WriteLine("Worker regression checks passed: stale freshness, explicit screen mapping, unconfirmed schedule, Intelisa upload rejection, and content versions.");
    }

    private static async Task ContentProcessorUsesLatestOutboxVersion()
    {
        await using var fixture = await WorkerFixture.CreateAsync();
        var ad = fixture.AddCampaign(fixture.Today, targetPlays: 5, actualPlays: 0);
        ad.ContentId = null;
        ad.ContentAssetVersion = null;
        ad.ContentState = "Uploading";
        ad.ReadyForScheduling = false;
        await fixture.Db.SaveChangesAsync();

        var intelisa = new IntelisaClient(new HttpClient(fixture.Intelisa), Options.Create(new IntelisaOptions
        {
            BaseUrl = "https://intelisa.test", UserName = "test-user", Password = "test-password"
        }));
        var processor = new ContentProcessor(fixture.Db, new BlobServiceClient(new Uri("https://storage.invalid")),
            intelisa, new ConfigurationBuilder().Build(), NullLogger<ContentProcessor>.Instance);
        var message = new ContentProcessingMessage("AdContentReady.v1", ad.Id, 1, "ads/test.mp4", null, DateTime.UtcNow);

        await processor.ProcessAsync(message, CancellationToken.None);
        Check(ad.ContentState == "Uploading", "A message without an outbox record must be ignored.");

        fixture.Db.ContentOutbox.Add(ContentOutbox.Create(ad.Id, 1, "ads/old.mp4", ad.Payment!.Id, DateTime.UtcNow));
        fixture.Db.ContentOutbox.Add(ContentOutbox.Create(ad.Id, 2, "ads/new.mp4", ad.Payment!.Id, DateTime.UtcNow));
        await fixture.Db.SaveChangesAsync();
        await processor.ProcessAsync(message, CancellationToken.None);
        Check(ad.ContentState == "Uploading", "An older asset version must be ignored.");
        var result = await processor.ProcessAsync(message with { AssetVersion = 2 }, CancellationToken.None);
        Check(result == ContentProcessingResult.Complete && ad.ContentState == "UploadUnconfirmed" && !string.IsNullOrEmpty(ad.ContentError),
            "The latest asset version must be processed and preserve an uncertain upload for reconciliation.");
    }

    private static async Task StaleProofUsesReportedFreshnessAndUnconfirmedDispatchIsPersisted()
    {
        await using var fixture = await WorkerFixture.CreateAsync();
        var today = fixture.Today;
        var advertisement = fixture.AddCampaign(today, targetPlays: 10, actualPlays: 0);
        advertisement.LastReportedDay = today.AddDays(-2);
        var previousRun = new ScheduleRun { RunDate = today.AddDays(-1), StartedAtUtc = DateTime.UtcNow.AddDays(-1) };
        fixture.Db.ScheduleRuns.Add(previousRun);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.AdScreenAllocations.Add(new AdScreenAllocation
        {
            RunId = previousRun.Id,
            AdvertisementId = advertisement.Id,
            ScreenId = "screen-1",
            ContentId = advertisement.ContentId!,
            PlayDate = today.AddDays(-1),
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(9, 0),
            PlannedPlays = 2,
            Status = "Sent",
            CreatedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        fixture.Intelisa.ReportLatestDay = today.AddDays(-2);
        fixture.Intelisa.ScheduleWriteStatus = HttpStatusCode.InternalServerError;
        var scheduler = fixture.CreateScheduler(new Dictionary<string, string[]> { [advertisement.TargetArea] = ["screen-1"] });
        await scheduler.RunAsync(CancellationToken.None);

        var savedAd = await fixture.Db.Advertisements.SingleAsync(item => item.Id == advertisement.Id);
        Check(savedAd.ActualPlays == 0, "Unreported days must not be added to actual plays.");
        Check(savedAd.LastReportedDay == today.AddDays(-2), "Scheduler must persist Intelisa's freshness watermark without advancing it to today.");
        var run = await fixture.Db.ScheduleRuns.SingleAsync(item => item.RunDate == today);
        var campaignRows = await fixture.Db.AdScreenAllocations
            .Where(item => item.RunId == run.Id && item.AdvertisementId == advertisement.Id).ToListAsync();
        Check(campaignRows.Count > 0, "A target-area campaign should create an explicit screen allocation.");
        Check(campaignRows.All(item => item.Status == "Unconfirmed"), "Failed responses after real schedule writes must be persisted as Unconfirmed.");
        Check(campaignRows.Sum(item => item.PlannedPlays) <= 8, "Unreported prior allocations must be reserved provisionally rather than treated as zero delivery.");
        var campaignRequests = fixture.Intelisa.ScheduleRequests.Where(request => request.ContentId == advertisement.ContentId).ToArray();
        Check(campaignRequests.Any(request => !request.DryRun), "Scheduler should submit the campaign schedule after preview.");
        Check(campaignRequests.Count(request => request.DryRun) == campaignRequests.Count(request => !request.DryRun),
            "Each real campaign write must be preceded by a dry-run preview.");
    }

    private static async Task MissingTargetAreaMappingBlocksPaidCreative()
    {
        await using var fixture = await WorkerFixture.CreateAsync();
        var advertisement = fixture.AddCampaign(fixture.Today, targetPlays: 5, actualPlays: 0);
        await fixture.Db.SaveChangesAsync();
        fixture.Intelisa.ReportLatestDay = fixture.Today.AddDays(-1);
        var scheduler = fixture.CreateScheduler(new Dictionary<string, string[]>());
        await scheduler.RunAsync(CancellationToken.None);

        var run = await fixture.Db.ScheduleRuns.SingleAsync(item => item.RunDate == fixture.Today);
        Check(run.BlockedCampaigns == 1, "Campaign without an explicit target-area screen mapping must be reported blocked.");
        Check(!await fixture.Db.AdScreenAllocations.AnyAsync(item => item.RunId == run.Id && item.AdvertisementId == advertisement.Id),
            "Campaign creative must not be scheduled to every available screen when target mapping is missing.");
        Check(fixture.Intelisa.ScheduleRequests.All(request => !request.ContentId.Equals(advertisement.ContentId, StringComparison.Ordinal)),
            "No Intelisa schedule request may contain the unmapped campaign creative.");
    }

    private static async Task IntelisaUploadRejectionIsClassifiedAsDefinitive()
    {
        var handler = new FakeIntelisaHandler
        {
            UploadStatusCode = HttpStatusCode.RequestEntityTooLarge
        };
        var client = new IntelisaClient(new HttpClient(handler), Options.Create(new IntelisaOptions
        {
            BaseUrl = "https://intelisa.test",
            UserName = "test-user",
            Password = "test-password"
        }));

        try
        {
            await client.UploadCreativeAsync(new MemoryStream([1, 2, 3]), "clip.mp4", "Brand - Test", CancellationToken.None);
            throw new InvalidOperationException("Expected Intelisa 413 rejection.");
        }
        catch (IntelisaRequestException exception)
        {
            Check(exception.StatusCode == HttpStatusCode.RequestEntityTooLarge, "Upload 413 must retain its HTTP status for the worker failure classification.");
            Check(!exception.OutcomeMayBeUnknown, "A definitive 413 response must not be marked as an uncertain upload.");
        }
        Check(handler.UploadRequests == 1, "A definitive Intelisa rejection must not be retried as another upload.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly string _databaseName;
        private WorkerFixture(string databaseName, AdRoverDbContext db, FakeIntelisaHandler intelisa)
        {
            _databaseName = databaseName;
            Db = db;
            Intelisa = intelisa;
        }

        public AdRoverDbContext Db { get; }
        public FakeIntelisaHandler Intelisa { get; }
        public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, FindIndiaTimeZone()));

        public static async Task<WorkerFixture> CreateAsync()
        {
            var databaseName = $"Adrover_WorkerTests_{Guid.NewGuid():N}";
            var options = new DbContextOptionsBuilder<AdRoverDbContext>()
                .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true")
                .Options;
            var db = new AdRoverDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var intelisa = new FakeIntelisaHandler();
            await SeedClientAsync(db);
            return new WorkerFixture(databaseName, db, intelisa);
        }

        public Advertisement AddCampaign(DateOnly today, long targetPlays, long actualPlays)
        {
            var user = Db.Users.Local.Single();
            var adId = Guid.NewGuid();
            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                AdvertisementId = adId,
                OrderId = $"test-{adId:N}",
                Status = PaymentStatus.Completed,
                PaymentMethod = "test",
                Description = "Worker regression fixture",
                Amount = 100,
                ProcessedAt = DateTime.UtcNow.AddDays(-7),
                CreatedAt = DateTime.UtcNow.AddDays(-7)
            };
            var advertisement = new Advertisement
            {
                Id = adId,
                ClientId = user.Id,
                Name = "Worker test campaign",
                Brand = "Test Brand",
                Description = "Worker regression fixture",
                TargetArea = "Test Area",
                VideoUrl = "private/test.mp4",
                VideoDurationSeconds = 15,
                ContentId = $"creative-{adId:N}",
                ContentAssetVersion = 1,
                ContentState = "Ready",
                ReadyForScheduling = true,
                ReadyForSchedulingAtUtc = DateTime.UtcNow.AddDays(-5),
                StartDate = today.AddDays(-3).ToDateTime(TimeOnly.MinValue),
                EndDate = today.AddDays(3).ToDateTime(TimeOnly.MinValue),
                TargetPlays = targetPlays,
                ActualPlays = actualPlays,
                AssignedCabs = 1,
                Status = AdvertisementStatus.Active,
                ApprovalAmount = 100,
                PaidAmount = 100,
                SubmittedAt = DateTime.UtcNow.AddDays(-7),
                Payment = payment
            };
            Db.Advertisements.Add(advertisement);
            return advertisement;
        }

        public DailyScheduler CreateScheduler(Dictionary<string, string[]> targetMappings)
        {
            var httpClient = new HttpClient(Intelisa)
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            var intelisaClient = new IntelisaClient(httpClient, Options.Create(new IntelisaOptions
            {
                BaseUrl = "https://intelisa.test",
                UserName = "test-user",
                Password = "test-password"
            }));
            var scheduling = new SchedulingOptions
            {
                OperatingHoursPerDay = 10,
                OperatingWindowStartTime = "08:00",
                OperatingWindowEndTime = "18:00",
                LoopSeconds = 180,
                ReservedSharePercent = 0,
                HouseAdContentId = "house-creative",
                TargetAreaScreenIds = new Dictionary<string, string[]>(targetMappings, StringComparer.OrdinalIgnoreCase)
            };
            return new DailyScheduler(Db, intelisaClient, Options.Create(scheduling), NullLogger<DailyScheduler>.Instance);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await using var cleanup = new AdRoverDbContext(new DbContextOptionsBuilder<AdRoverDbContext>()
                .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={_databaseName};Integrated Security=true;TrustServerCertificate=true")
                .Options);
            await cleanup.Database.EnsureDeletedAsync();
        }

        private static async Task SeedClientAsync(AdRoverDbContext db)
        {
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Email = $"worker-{Guid.NewGuid():N}@example.test",
                Phone = $"9{Random.Shared.NextInt64(100000000, 999999999)}",
                FullName = "Worker Test",
                PasswordHash = "not-used",
                Role = UserRole.Client,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        private static TimeZoneInfo FindIndiaTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
        }
    }

    private sealed class FakeIntelisaHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        public DateOnly ReportLatestDay { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        public HttpStatusCode ScheduleWriteStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode UploadStatusCode { get; set; } = HttpStatusCode.Created;
        public int UploadRequests { get; private set; }
        public int RealScheduleWrites { get; private set; }
        public int ScheduleDryRuns { get; private set; }
        public List<(string ContentId, bool DryRun)> ScheduleRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
                return Json(HttpStatusCode.OK, new { token = "fake-token", expiresIn = 86400 });
            if (request.Headers.GetValues("auth-token").SingleOrDefault() != "fake-token")
                return Json(HttpStatusCode.Unauthorized, new { message = "missing auth" });
            if (path == "/api/v1/screens")
                return Json(HttpStatusCode.OK, new[]
                {
                    new { screenID = "screen-1", screenName = "Test screen", spotCode = "Test Area", status = "ON", lastDataRecorded = "2026-11-24 08:00", tags = Array.Empty<string>() }
                });
            if (path == "/api/v1/reports/creative-detail")
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
                var start = DateOnly.Parse(body.GetProperty("start_date").GetString()!);
                var end = DateOnly.Parse(body.GetProperty("end_date").GetString()!);
                var dates = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1)
                    .Select(offset => start.AddDays(offset).ToString("yyyy-MM-dd"))
                    .ToArray();
                var perDay = dates.Select(date => new { date, plays = 0, minutes = 0m }).ToArray();
                return Json(HttpStatusCode.OK, new
                {
                    totals = new { plays = 0, minutes = 0m },
                    per_day = perDay,
                    dates,
                    screens = Array.Empty<object>(),
                    data_freshness = new { latest_day_with_play_data = ReportLatestDay.ToString("yyyy-MM-dd") }
                });
            }
            if (path == "/api/v1/schedules")
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
                var contentId = body.GetProperty("content")[0].GetProperty("id").GetString()!;
                var dryRun = body.TryGetProperty("dry_run", out var dry) && dry.GetBoolean();
                ScheduleRequests.Add((contentId, dryRun));
                if (dryRun)
                {
                    ScheduleDryRuns++;
                    return Json(HttpStatusCode.OK, new { status = "preview" });
                }
                RealScheduleWrites++;
                return Json(ScheduleWriteStatus, new { message = "simulated provider response" });
            }
            if (path == "/api/v1/creatives/upload")
            {
                UploadRequests++;
                return Json(UploadStatusCode, new { message = "simulated upload response", creative = new { id = "creative-upload-test" } });
            }
            return Json(HttpStatusCode.NotFound, new { message = "unexpected path" });
        }

        private static HttpResponseMessage Json<T>(HttpStatusCode status, T body) => new(status)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
    }
}
