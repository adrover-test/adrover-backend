namespace Adrover.Functions.Configuration;

public sealed class IntelisaOptions
{
    public string BaseUrl { get; set; } = "https://login.intelisa.in";
    public string? UserName { get; set; }
    public string? Password { get; set; }
}

public sealed class ServiceBusOptions
{
    public const string QueueName = "ad-content-processing";
    public string? ConnectionString { get; set; }
    public string? FullyQualifiedNamespace { get; set; }
}

public sealed class SchedulingOptions
{
    public int OperatingHoursPerDay { get; set; } = 10;
    public string OperatingWindowStartTime { get; set; } = "08:00";
    public string OperatingWindowEndTime { get; set; } = "18:00";
    public int LoopSeconds { get; set; } = 180;
    public int ReservedSharePercent { get; set; } = 20;
    public string[] ReservedContentIds { get; set; } = [];
    public string? HouseAdContentId { get; set; }
    public bool MinuteScheduling { get; set; }
    public Dictionary<string, string[]> TargetAreaScreenIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
