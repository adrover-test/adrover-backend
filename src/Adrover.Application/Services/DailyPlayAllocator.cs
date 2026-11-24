using System.Numerics;

namespace Adrover.Application.Services;

public sealed record CampaignPlayDemand(
    Guid AdvertisementId,
    DateOnly StartDate,
    DateOnly EndDate,
    long TotalContractedPlays,
    long CompletedPlays,
    int PlayDurationSeconds);

public sealed record ScreenCommercialCapacity(string ScreenId, long AvailableSeconds);

public sealed record ScreenCampaignAllocation(Guid AdvertisementId, string ScreenId, long PlannedPlays);

public sealed record ScreenHouseAdAllocation(string ScreenId, long AvailableSeconds);

public sealed record CampaignDailyAllocation(
    Guid AdvertisementId,
    long DailyTarget,
    long ScheduledPlays,
    long Shortfall);

public sealed record DailyPlayAllocationPlan(
    DateOnly Date,
    IReadOnlyList<CampaignDailyAllocation> Campaigns,
    IReadOnlyList<ScreenCampaignAllocation> ScreenAllocations,
    IReadOnlyList<ScreenHouseAdAllocation> HouseAdAllocations);

public static class DailyPlayAllocator
{
    public const int DefaultOperatingHoursPerDay = 10;
    public const int DefaultLoopSeconds = 180;
    public const int DefaultReservedSharePercent = 20;

    private sealed record DemandTarget(CampaignPlayDemand Campaign, long Target);

    public static long CalculateCommercialSecondsPerScreen(
        int operatingHoursPerDay = DefaultOperatingHoursPerDay,
        int loopSeconds = DefaultLoopSeconds,
        int reservedSharePercent = DefaultReservedSharePercent)
    {
        if (operatingHoursPerDay is < 1 or > 24)
            throw new ArgumentOutOfRangeException(nameof(operatingHoursPerDay));
        if (loopSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(loopSeconds));
        if (reservedSharePercent is < 0 or > 90)
            throw new ArgumentOutOfRangeException(nameof(reservedSharePercent));

        var loopsPerDay = (long)operatingHoursPerDay * 3600 / loopSeconds;
        var reservedSecondsPerLoop = (int)Math.Ceiling(loopSeconds * reservedSharePercent / 100m);
        return checked((loopSeconds - reservedSecondsPerLoop) * loopsPerDay);
    }

    public static DailyPlayAllocationPlan Allocate(
        DateOnly today,
        IEnumerable<CampaignPlayDemand> campaigns,
        IEnumerable<ScreenCommercialCapacity> screens)
    {
        ArgumentNullException.ThrowIfNull(campaigns);
        ArgumentNullException.ThrowIfNull(screens);

        var campaignList = campaigns.ToList();
        var screenList = screens.ToList();
        Validate(campaignList, screenList);

        var capacities = screenList.ToDictionary(screen => screen.ScreenId, screen => screen.AvailableSeconds, StringComparer.Ordinal);
        var activeDemands = campaignList
            .Select(campaign => new DemandTarget(campaign, GetDailyTarget(today, campaign)))
            .ToList();

        var totalCapacity = capacities.Values.Aggregate(0L, (total, capacity) => checked(total + capacity));
        var totalDemandSeconds = activeDemands.Aggregate(BigInteger.Zero,
            (total, demand) => total + (BigInteger)demand.Target * demand.Campaign.PlayDurationSeconds);
        var allocatedTargets = ScaleToCapacity(activeDemands, totalDemandSeconds, totalCapacity);

        var screenAllocations = new List<ScreenCampaignAllocation>();
        var scheduledByCampaign = new Dictionary<Guid, long>();
        foreach (var demand in activeDemands
                     .OrderBy(item => item.Campaign.EndDate)
                     .ThenBy(item => item.Campaign.AdvertisementId))
        {
            var target = allocatedTargets[demand.Campaign.AdvertisementId];
            var placed = DistributeAcrossScreens(demand.Campaign.AdvertisementId, target,
                demand.Campaign.PlayDurationSeconds, capacities, screenAllocations);
            scheduledByCampaign[demand.Campaign.AdvertisementId] = placed;
        }

        foreach (var demand in activeDemands.OrderBy(item => item.Campaign.EndDate).ThenBy(item => item.Campaign.AdvertisementId))
        {
            var shortfall = demand.Target - scheduledByCampaign[demand.Campaign.AdvertisementId];
            var placed = DistributeAcrossScreens(demand.Campaign.AdvertisementId, shortfall,
                demand.Campaign.PlayDurationSeconds, capacities, screenAllocations);
            scheduledByCampaign[demand.Campaign.AdvertisementId] += placed;
        }

        var campaignResults = activeDemands
            .OrderBy(item => item.Campaign.AdvertisementId)
            .Select(item =>
            {
                var scheduled = scheduledByCampaign[item.Campaign.AdvertisementId];
                return new CampaignDailyAllocation(
                    item.Campaign.AdvertisementId,
                    item.Target,
                    scheduled,
                    item.Target - scheduled);
            })
            .ToArray();

        var houseAdAllocations = capacities
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ScreenHouseAdAllocation(pair.Key, pair.Value))
            .ToArray();

        var combinedAllocations = screenAllocations
            .GroupBy(item => (item.AdvertisementId, item.ScreenId))
            .Select(group => new ScreenCampaignAllocation(group.Key.AdvertisementId, group.Key.ScreenId,
                group.Sum(item => item.PlannedPlays)))
            .ToArray();
        return new DailyPlayAllocationPlan(today, campaignResults, combinedAllocations, houseAdAllocations);
    }

    private static long GetDailyTarget(DateOnly today, CampaignPlayDemand campaign)
    {
        if (today < campaign.StartDate || today > campaign.EndDate)
            return 0;

        var remainingPlays = Math.Max(0, campaign.TotalContractedPlays - Math.Min(campaign.CompletedPlays, campaign.TotalContractedPlays));
        if (remainingPlays == 0)
            return 0;

        var firstRemainingDay = today > campaign.StartDate ? today : campaign.StartDate;
        var remainingDays = campaign.EndDate.DayNumber - firstRemainingDay.DayNumber + 1;
        return remainingPlays / remainingDays + (remainingPlays % remainingDays == 0 ? 0 : 1);
    }

    private static Dictionary<Guid, long> ScaleToCapacity(
        IReadOnlyList<DemandTarget> demands,
        BigInteger totalDemandSeconds,
        long totalCapacitySeconds)
    {
        var result = demands.ToDictionary(demand => demand.Campaign.AdvertisementId, demand => demand.Target);
        if (totalDemandSeconds <= totalCapacitySeconds || totalDemandSeconds.IsZero)
            return result;

        var portions = demands.Select(demand =>
        {
            var numerator = (BigInteger)demand.Target * totalCapacitySeconds;
            return new
            {
                demand.Campaign.AdvertisementId,
                demand.Target,
                demand.Campaign.PlayDurationSeconds,
                Plays = (long)(numerator / totalDemandSeconds),
                Remainder = numerator % totalDemandSeconds
            };
        }).ToList();

        var allocatedSeconds = portions.Aggregate(BigInteger.Zero,
            (total, portion) => total + (BigInteger)portion.Plays * portion.PlayDurationSeconds);
        foreach (var portion in portions)
            result[portion.AdvertisementId] = portion.Plays;

        foreach (var portion in portions.OrderByDescending(item => item.Remainder).ThenBy(item => item.AdvertisementId))
        {
            if (portion.Plays >= portion.Target || allocatedSeconds + portion.PlayDurationSeconds > totalCapacitySeconds)
                continue;

            result[portion.AdvertisementId] = portion.Plays + 1;
            allocatedSeconds += portion.PlayDurationSeconds;
        }

        return result;
    }

    private static long DistributeAcrossScreens(
        Guid advertisementId,
        long plays,
        int playDurationSeconds,
        IDictionary<string, long> remainingCapacity,
        ICollection<ScreenCampaignAllocation> allocations)
    {
        var availableScreens = remainingCapacity
            .Where(pair => pair.Value >= playDurationSeconds)
            .Select(pair => new { pair.Key, Capacity = pair.Value / playDurationSeconds })
            .ToList();
        var totalCapacity = availableScreens.Aggregate(BigInteger.Zero, (total, screen) => total + screen.Capacity);
        if (plays == 0 || totalCapacity.IsZero)
            return 0;

        var planned = (long)BigInteger.Min(plays, totalCapacity);
        var portions = availableScreens.Select(screen =>
        {
            var weighted = (BigInteger)planned * screen.Capacity;
            return new
            {
                screen.Key,
                screen.Capacity,
                Plays = (long)(weighted / totalCapacity),
                Remainder = weighted % totalCapacity
            };
        }).ToList();
        var assigned = portions.Sum(portion => portion.Plays);
        var extras = portions.OrderByDescending(portion => portion.Remainder)
            .ThenBy(portion => portion.Key, StringComparer.Ordinal)
            .Take(checked((int)(planned - assigned)))
            .Select(portion => portion.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var portion in portions.OrderBy(portion => portion.Key, StringComparer.Ordinal))
        {
            var count = portion.Plays + (extras.Contains(portion.Key) ? 1 : 0);
            if (count == 0)
                continue;
            allocations.Add(new ScreenCampaignAllocation(advertisementId, portion.Key, count));
            remainingCapacity[portion.Key] -= checked(count * playDurationSeconds);
        }

        return planned;
    }

    private static void Validate(
        IReadOnlyCollection<CampaignPlayDemand> campaigns,
        IReadOnlyCollection<ScreenCommercialCapacity> screens)
    {
        if (campaigns.Any(campaign => campaign.AdvertisementId == Guid.Empty ||
                                      campaign.StartDate > campaign.EndDate ||
                                      campaign.TotalContractedPlays < 0 || campaign.CompletedPlays < 0 ||
                                      campaign.PlayDurationSeconds < 5))
            throw new ArgumentException("Campaign IDs, dates, and play counts must be valid.", nameof(campaigns));

        if (campaigns.Select(campaign => campaign.AdvertisementId).Distinct().Count() != campaigns.Count)
            throw new ArgumentException("Campaign IDs must be unique.", nameof(campaigns));

        if (screens.Any(screen => string.IsNullOrWhiteSpace(screen.ScreenId) || screen.AvailableSeconds < 0))
            throw new ArgumentException("Screen IDs and commercial capacities must be valid.", nameof(screens));

        if (screens.Select(screen => screen.ScreenId).Distinct(StringComparer.Ordinal).Count() != screens.Count)
            throw new ArgumentException("Screen IDs must be unique.", nameof(screens));
    }
}
