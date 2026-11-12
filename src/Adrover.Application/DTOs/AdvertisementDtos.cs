namespace Adrover.Application.DTOs.Advertisement;

public class CreateAdvertisementRequest
{
    public string Name { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TargetArea { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public long TargetPlays { get; set; }
    public int AssignedCabs { get; set; }
}

public class UpdateAdvertisementRequest
{
    public string Name { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TargetArea { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public long TargetPlays { get; set; }
    public int AssignedCabs { get; set; }
}

public class AdvertisementDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TargetArea { get; set; } = string.Empty;
    public string? VideoUrl { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public long TargetPlays { get; set; }
    public long ActualPlays { get; set; }
    public int AssignedCabs { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal ApprovalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public string? PaymentStatus { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ApproveAdvertisementRequest
{
    public decimal ApprovalAmount { get; set; }
    public long EstimatedPlays { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class RejectAdvertisementRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class AdvertisementListResponse
{
    public List<AdvertisementDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}
