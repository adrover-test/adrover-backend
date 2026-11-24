namespace Adrover.Domain.Entities;

public sealed class ContentOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AdvertisementId { get; set; }
    public int AssetVersion { get; set; }
    public string MessageId { get; set; } = string.Empty;
    public string VideoBlobPath { get; set; } = string.Empty;
    public Guid? PaymentId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    public static ContentOutbox Create(
        Guid advertisementId,
        int assetVersion,
        string videoBlobPath,
        Guid? paymentId,
        DateTime nowUtc) => new()
    {
        AdvertisementId = advertisementId,
        AssetVersion = assetVersion,
        MessageId = $"content-{advertisementId:D}-v{assetVersion}",
        VideoBlobPath = videoBlobPath,
        PaymentId = paymentId,
        CreatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
        NextAttemptAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)
    };
}

public sealed class ScheduleRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly RunDate { get; set; }
    public string Status { get; set; } = "Running";
    public int AdsScheduled { get; set; }
    public int BlockedCampaigns { get; set; }
    public int ScreensUsed { get; set; }
    public long PlannedPlays { get; set; }
    public long ShortfallPlays { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string? LastError { get; set; }
}

public sealed class AdScreenAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid? AdvertisementId { get; set; }
    public string ScreenId { get; set; } = string.Empty;
    public string ContentId { get; set; } = string.Empty;
    public DateOnly PlayDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public long PlannedPlays { get; set; }
    public string Status { get; set; } = "Planned";
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class AdDailyDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AdvertisementId { get; set; }
    public string ScreenId { get; set; } = string.Empty;
    public DateOnly PlayDate { get; set; }
    public long Plays { get; set; }
    public decimal? Minutes { get; set; }
    public DateTime ReportedAtUtc { get; set; }
}
