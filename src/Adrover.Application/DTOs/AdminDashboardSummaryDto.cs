namespace Adrover.Application.DTOs;

public sealed class AdminDashboardSummaryDto
{
    public int ClientsPresent { get; set; }
    public int Enquiries { get; set; }
    public int CabsInDirectory { get; set; }
    public int AdsAwaitingApproval { get; set; }
    public int TotalAds { get; set; }
    public long TotalAdPlays { get; set; }
    public decimal TotalRevenueTillDate { get; set; }
}
