namespace Adrover.Domain.Entities;

public class DriverAssignment
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public Guid AdvertisementId { get; set; }
    public DateTime ScheduledAt { get; set; }
    public long TargetPlays { get; set; }
    public decimal RatePerPlay { get; set; }
    public string Status { get; set; } = "assigned";
    public DateTime CreatedAt { get; set; }
    public Driver Driver { get; set; } = null!;
    public Advertisement Advertisement { get; set; } = null!;
}

public class DriverPlayback
{
    public Guid Id { get; set; }
    public Guid AssignmentId { get; set; }
    public Guid RequestId { get; set; }
    public long Plays { get; set; }
    public decimal Amount { get; set; }
    public DateTime PlayedAt { get; set; }
    public DriverAssignment Assignment { get; set; } = null!;
}

public class DriverPayout
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = "pending";
    public string? Reference { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime ScheduledAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DriverNotification
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public class DriverIssue
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public Guid RequestId { get; set; }
    public Guid? AdvertisementId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "submitted";
    public string Language { get; set; } = "en";
    public string ContactMobile { get; set; } = string.Empty;
    public string VehicleRegistration { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public string? AttachmentBlobName { get; set; }
    public string? AdminResponse { get; set; }
    public bool Confirmed { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<DriverIssueHistory> History { get; set; } = new List<DriverIssueHistory>();
}

public class DriverIssueHistory
{
    public Guid Id { get; set; }
    public Guid IssueId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string By { get; set; } = string.Empty;
    public DateTime At { get; set; }
}
