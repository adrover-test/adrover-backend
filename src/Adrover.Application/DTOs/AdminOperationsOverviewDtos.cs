namespace Adrover.Application.DTOs;

public sealed class AdminMonthlyAdPlaysPointDto
{
    public string Month { get; set; } = string.Empty;
    public long TotalPlays { get; set; }
}

public sealed class AdminMonthlyRevenuePointDto
{
    public string Month { get; set; } = string.Empty;
    public decimal NetRevenue { get; set; }
}

public sealed class AdminAdvertisementStatusPointDto
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class AdminOperationsOverviewDto
{
    public string Currency { get; set; } = "INR";
    public string Range { get; set; } = "monthly";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public long TotalAdPlaysInPeriod { get; set; }
    public decimal NetRevenueInPeriod { get; set; }
    public int TotalAds { get; set; }
    public List<AdminMonthlyAdPlaysPointDto> MonthlyAdPlays { get; set; } = new();
    public List<AdminMonthlyRevenuePointDto> MonthlyCampaignRevenue { get; set; } = new();
    public List<AdminAdvertisementStatusPointDto> AdvertisementsByStatus { get; set; } = new();
}
