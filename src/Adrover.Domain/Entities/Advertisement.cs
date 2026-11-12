namespace Adrover.Domain.Entities;

/// <summary>
/// Advertisement status enumeration
/// </summary>
public enum AdvertisementStatus
{
    PendingApproval = 1,
    Approved = 2,
    Active = 3,
    Completed = 4,
    Rejected = 5,
    Cancelled = 6
}

/// <summary>
/// Represents an advertisement campaign
/// </summary>
public class Advertisement
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TargetArea { get; set; } = string.Empty;
    
    public string? VideoUrl { get; set; }
    public string? ImageUrl { get; set; }
    public decimal VideoDurationSeconds { get; set; }
    
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public long TargetPlays { get; set; }
    public long ActualPlays { get; set; }
    public int AssignedCabs { get; set; }
    
    public AdvertisementStatus Status { get; set; } = AdvertisementStatus.PendingApproval;
    
    public decimal ApprovalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public Guid? PaymentId { get; set; }
    
    public string? RejectionReason { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    
    public DateTime SubmittedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual User? Client { get; set; }
    public virtual Payment? Payment { get; set; }
    public virtual ICollection<AdPlay> AdPlays { get; set; } = new List<AdPlay>();
}

/// <summary>
/// Tracks individual advertisement play events
/// </summary>
public class AdPlay
{
    public Guid Id { get; set; }
    public Guid AdvertisementId { get; set; }
    public string DriverPhone { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int DurationSeconds { get; set; }
    public DateTime PlayedAt { get; set; }
    
    // Navigation properties
    public virtual Advertisement? Advertisement { get; set; }
}
