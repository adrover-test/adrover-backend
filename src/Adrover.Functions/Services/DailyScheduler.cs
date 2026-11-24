using System.Data;
using System.Globalization;
using System.Net;
using Adrover.Application.Services;
using Adrover.Data.Context;
using Adrover.Domain.Entities;
using Adrover.Functions.Configuration;
using Adrover.Functions.Intelisa;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Adrover.Functions.Services;

public sealed class DailyScheduler(
    AdRoverDbContext db,
    IntelisaClient intelisa,
    IOptions<SchedulingOptions> options,
    ILogger<DailyScheduler> logger)
{
    private static readonly TimeZoneInfo IndiaTimeZone = FindIndiaTimeZone();
    private readonly SchedulingOptions _options = options.Value;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var lockAcquired = false;
        try
        {
            await AcquireSessionLockAsync(cancellationToken);
            lockAcquired = true;
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IndiaTimeZone));
            var run = await db.ScheduleRuns.SingleOrDefaultAsync(item => item.RunDate == today, cancellationToken);
            if (run is null)
            {
                run = new ScheduleRun { RunDate = today, StartedAtUtc = DateTime.UtcNow };
                db.ScheduleRuns.Add(run);
                await db.SaveChangesAsync(cancellationToken);
            }

            var screens = (await intelisa.GetScreensAsync(cancellationToken))
                .Where(IsRecentlyAvailable)
                .ToDictionary(screen => screen.ScreenId, StringComparer.Ordinal);
            if (screens.Count == 0)
            {
                run.Status = "Failed";
                run.LastError = "Intelisa returned no screens that are online or recently active.";
                run.FinishedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            var advertisements = await db.Advertisements
                .Include(item => item.Payment)
                .Where(item => item.Status == AdvertisementStatus.Active || item.Status == AdvertisementStatus.Started ||
                               (item.Status == AdvertisementStatus.Completed && item.ContentId != null))
                .OrderBy(item => item.EndDate)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

            await RefreshProofOfPlayAsync(advertisements, today, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            if (!await db.AdScreenAllocations.AnyAsync(item => item.RunId == run.Id, cancellationToken))
                await CreatePlanAsync(run, advertisements, screens, today, cancellationToken);

            var staleSubmissions = await db.AdScreenAllocations
                .Where(item => item.RunId == run.Id && item.Status == "Submitting")
                .ToListAsync(cancellationToken);
            foreach (var item in staleSubmissions)
            {
                item.Status = "Unconfirmed";
                item.LastError = "The scheduler stopped during an external write; Intelisa may have applied the schedule. Reconcile manually before retrying.";
                item.UpdatedAtUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);

            var pending = await db.AdScreenAllocations
                .Where(item => item.RunId == run.Id && item.Status == "Planned")
                .OrderBy(item => item.ScreenId)
                .ThenBy(item => item.StartTime)
                .ToListAsync(cancellationToken);
            var dispatchGroups = pending.GroupBy(item => new
            {
                item.AdvertisementId,
                item.ContentId,
                item.PlayDate,
                item.StartTime,
                item.EndTime
            });
            foreach (var group in dispatchGroups)
            {
                foreach (var batch in group.Chunk(500))
                    await DispatchBatchAsync(batch, cancellationToken);
            }

            var outcomes = await db.AdScreenAllocations.Where(item => item.RunId == run.Id).ToListAsync(cancellationToken);
            run.AdsScheduled = outcomes.Where(item => item.AdvertisementId != null && item.Status == "Sent")
                .Select(item => item.AdvertisementId).Distinct().Count();
            run.ScreensUsed = outcomes.Where(item => item.Status == "Sent").Select(item => item.ScreenId).Distinct().Count();
            run.PlannedPlays = outcomes.Where(item => item.AdvertisementId != null).Sum(item => item.PlannedPlays);
            run.FinishedAtUtc = DateTime.UtcNow;
            run.Status = run.BlockedCampaigns > 0 || outcomes.Any(item => item.Status is "Failed" or "Unconfirmed")
                ? "PartiallySucceeded"
                : "Succeeded";
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            if (lockAcquired)
                await ReleaseSessionLockAsync(CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task RefreshProofOfPlayAsync(
        IReadOnlyCollection<Advertisement> advertisements,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var yesterday = today.AddDays(-1);
        foreach (var advertisement in advertisements)
        {
            var campaignEnd = DateOnly.FromDateTime(advertisement.EndDate);
            if (advertisement.Status == AdvertisementStatus.Completed &&
                (advertisement.ActualPlays >= advertisement.TargetPlays ||
                 (advertisement.LastReportedDay.HasValue && advertisement.LastReportedDay.Value >= campaignEnd)))
                continue;

            var startDate = DateOnly.FromDateTime(advertisement.StartDate);
            var hasStarted = advertisement.FirstPlaybackDate.HasValue || CampaignSetup.HasStarted(advertisement);
            var incompleteOverdueSetup = !hasStarted && !IsSetupReadyInTime(advertisement, startDate);
            if (advertisement.ActualPlays >= advertisement.TargetPlays ||
                (DateOnly.FromDateTime(advertisement.EndDate) < today && !incompleteOverdueSetup))
                advertisement.Status = AdvertisementStatus.Completed;
            if (string.IsNullOrWhiteSpace(advertisement.ContentId))
                continue;

            var campaignStart = DateOnly.FromDateTime(advertisement.StartDate);
            var reportEnd = campaignEnd < yesterday ? campaignEnd : yesterday;
            if (campaignStart > reportEnd)
                continue;

            var report = await intelisa.GetCreativeReportAsync(advertisement.ContentId!, campaignStart, reportEnd, cancellationToken);
            if (report.DataFreshness?.LatestDayWithPlayData is not { } latestText ||
                !DateOnly.TryParseExact(latestText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var latestReportedDate))
                continue;
            if (latestReportedDate > reportEnd)
                latestReportedDate = reportEnd;
            if (advertisement.LastReportedDay.HasValue && latestReportedDate < advertisement.LastReportedDay.Value)
                continue;

            if (report.PerDay is null)
                continue;
            advertisement.LastReportedDay = latestReportedDate;
            var trustedDays = report.PerDay
                .Select(day => (Day: ParseDate(day.Date), day.Plays))
                .Where(item => item.Day <= latestReportedDate && item.Day <= reportEnd)
                .OrderBy(item => item.Day)
                .ToArray();
            advertisement.ActualPlays = trustedDays.Sum(item => item.Plays);

            if (advertisement.FirstPlaybackDate is null)
            {
                var firstPlayedDate = trustedDays.FirstOrDefault(item => item.Plays > 0).Day;
                if (firstPlayedDate != default)
                {
                    advertisement.FirstPlaybackDate = firstPlayedDate;
                    advertisement.Status = AdvertisementStatus.Started;
                }
            }

            var dates = report.Dates ?? [];
            foreach (var screen in report.Screens ?? [])
            {
                if (screen.DailyPlays is null)
                    continue;
                var count = Math.Min(dates.Length, screen.DailyPlays.Length);
                for (var index = 0; index < count; index++)
                {
                    var playDate = ParseDate(dates[index]);
                    if (playDate > latestReportedDate || playDate > reportEnd)
                        continue;
                    var delivery = await db.AdDailyDeliveries.SingleOrDefaultAsync(item =>
                        item.AdvertisementId == advertisement.Id && item.ScreenId == screen.ScreenId && item.PlayDate == playDate,
                        cancellationToken);
                    if (delivery is null)
                    {
                        delivery = new AdDailyDelivery
                        {
                            AdvertisementId = advertisement.Id,
                            ScreenId = screen.ScreenId,
                            PlayDate = playDate
                        };
                        db.AdDailyDeliveries.Add(delivery);
                    }
                    delivery.Plays = Math.Max(0, screen.DailyPlays[index]);
                    delivery.Minutes = null;
                    delivery.ReportedAtUtc = DateTime.UtcNow;
                }
            }

            var hasStartedAfterReport = advertisement.FirstPlaybackDate.HasValue || CampaignSetup.HasStarted(advertisement);
            var incompleteOverdueSetupAfterReport = !hasStartedAfterReport && !IsSetupReadyInTime(advertisement, campaignStart);
            if (advertisement.ActualPlays >= advertisement.TargetPlays ||
                (DateOnly.FromDateTime(advertisement.EndDate) < today && !incompleteOverdueSetupAfterReport))
                advertisement.Status = AdvertisementStatus.Completed;
        }
    }

    private async Task CreatePlanAsync(
        ScheduleRun run,
        IReadOnlyCollection<Advertisement> advertisements,
        IReadOnlyDictionary<string, IntelisaScreen> screens,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var liveCampaigns = new List<(Advertisement Ad, string[] ScreenIds, long EffectiveCompletedPlays)>();
        var blocked = 0;
        foreach (var ad in advertisements)
        {
            if (ad.Status is not (AdvertisementStatus.Active or AdvertisementStatus.Started) ||
                ad.Payment?.Status != PaymentStatus.Completed || ad.PaidAmount < ad.ApprovalAmount ||
                !ad.ReadyForScheduling || string.IsNullOrWhiteSpace(ad.ContentId) ||
                DateOnly.FromDateTime(ad.StartDate) > today || DateOnly.FromDateTime(ad.EndDate) < today ||
                ad.ActualPlays >= ad.TargetPlays)
                continue;

            var startDate = DateOnly.FromDateTime(ad.StartDate);
            if (!IsSetupReadyInTime(ad, startDate))
            {
                blocked++;
                logger.LogWarning("Skipping advertisement {AdvertisementId}: setup became complete after its start date.", ad.Id);
                continue;
            }

            var provisionalPlays = await GetProvisionalPlaysAsync(ad, startDate, today.AddDays(-1), cancellationToken);
            if (provisionalPlays is null)
            {
                blocked++;
                logger.LogWarning("Skipping advertisement {AdvertisementId}: Intelisa proof-of-play freshness is insufficient and prior allocations cannot be reserved safely.", ad.Id);
                continue;
            }
            var effectiveCompleted = Math.Min(ad.TargetPlays, checked(ad.ActualPlays + provisionalPlays.Value));

            if (!_options.TargetAreaScreenIds.TryGetValue(ad.TargetArea, out var configuredScreenIds) || configuredScreenIds.Length == 0)
            {
                blocked++;
                logger.LogWarning("Skipping advertisement {AdvertisementId}: no explicit screen mapping is configured for target area {TargetArea}.", ad.Id, ad.TargetArea);
                continue;
            }

            var requestedIds = configuredScreenIds.Distinct(StringComparer.Ordinal).Where(screens.ContainsKey).ToArray();
            if (requestedIds.Length == 0)
            {
                blocked++;
                logger.LogWarning("Skipping advertisement {AdvertisementId}: no configured screen for target area {TargetArea} is available in Intelisa.", ad.Id, ad.TargetArea);
                continue;
            }
            liveCampaigns.Add((ad, requestedIds, effectiveCompleted));
        }

        run.BlockedCampaigns = blocked;
        var screenCapacities = screens.Keys.ToDictionary(
            id => id,
            _ => DailyPlayAllocator.CalculateCommercialSecondsPerScreen(
                _options.OperatingHoursPerDay, _options.LoopSeconds, _options.ReservedSharePercent),
            StringComparer.Ordinal);
        var campaignScreenAllocations = new List<(Advertisement Ad, string ScreenId, long Plays)>();
        var shortfall = 0L;

        var groups = liveCampaigns.GroupBy(item => string.Join("|", item.ScreenIds.OrderBy(id => id, StringComparer.Ordinal)), StringComparer.Ordinal)
            .OrderBy(group => group.Min(item => item.Ad.EndDate))
            .ThenBy(group => group.Key, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var mappedIds = group.First().ScreenIds;
            var demands = group.Select(item => new CampaignPlayDemand(
                item.Ad.Id,
                DateOnly.FromDateTime(item.Ad.StartDate),
                DateOnly.FromDateTime(item.Ad.EndDate),
                item.Ad.TargetPlays,
                item.EffectiveCompletedPlays,
                Math.Max(5, (int)Math.Ceiling(item.Ad.VideoDurationSeconds)))).ToArray();
            var capacities = mappedIds.Select(id => new ScreenCommercialCapacity(id, screenCapacities[id])).ToArray();
            var plan = DailyPlayAllocator.Allocate(today, demands, capacities);
            foreach (var allocation in plan.ScreenAllocations)
            {
                var ad = group.Single(item => item.Ad.Id == allocation.AdvertisementId).Ad;
                campaignScreenAllocations.Add((ad, allocation.ScreenId, allocation.PlannedPlays));
                screenCapacities[allocation.ScreenId] -= allocation.PlannedPlays * Math.Max(5, (int)Math.Ceiling(ad.VideoDurationSeconds));
            }
            shortfall += plan.Campaigns.Sum(item => item.Shortfall);
        }
        run.ShortfallPlays = shortfall;

        var totalStart = ParseTime(_options.OperatingWindowStartTime);
        var totalEnd = ParseTime(_options.OperatingWindowEndTime);
        var windowMinutes = (int)(totalEnd - totalStart).TotalMinutes;
        var reservedSeconds = (long)Math.Ceiling(_options.LoopSeconds * _options.ReservedSharePercent / 100m) *
                              (_options.OperatingHoursPerDay * 3600L / _options.LoopSeconds);
        var reservedMinutes = Math.Min(windowMinutes, (int)Math.Ceiling(reservedSeconds / 60m));
        if (!_options.MinuteScheduling)
            reservedMinutes = Math.Min(windowMinutes, (int)Math.Ceiling(reservedMinutes / 60m) * 60);
        var commercialStart = totalStart.AddMinutes(reservedMinutes);
        var reservedContent = _options.ReservedContentIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (reservedContent.Length == 0 && !string.IsNullOrWhiteSpace(_options.HouseAdContentId))
            reservedContent = [_options.HouseAdContentId];

        foreach (var screenId in screens.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (reservedMinutes > 0 && reservedContent.Length > 0)
                AddAllocation(run, null, screenId, reservedContent, today, totalStart, commercialStart, 0);

            var cursor = commercialStart;
            foreach (var planned in campaignScreenAllocations.Where(item => item.ScreenId == screenId)
                         .OrderBy(item => item.Ad.EndDate).ThenBy(item => item.Ad.Id))
            {
                var remainingMinutes = (int)(totalEnd - cursor).TotalMinutes;
                if (remainingMinutes <= 0)
                {
                    run.ShortfallPlays += planned.Plays;
                    continue;
                }
                var requestedMinutes = (int)Math.Ceiling(planned.Plays * (double)_options.LoopSeconds / 60d);
                if (!_options.MinuteScheduling)
                    requestedMinutes = (int)Math.Ceiling(requestedMinutes / 60d) * 60;
                var slotMinutes = Math.Min(remainingMinutes, requestedMinutes);
                var estimatedPlays = Math.Min(planned.Plays, (long)slotMinutes * 60 / _options.LoopSeconds);
                run.ShortfallPlays += planned.Plays - estimatedPlays;
                if (estimatedPlays <= 0)
                    continue;
                var end = cursor.AddMinutes(slotMinutes);
                AddAllocation(run, planned.Ad, screenId, [planned.Ad.ContentId!], today, cursor, end, estimatedPlays);
                cursor = end;
            }

            if (cursor < totalEnd && !string.IsNullOrWhiteSpace(_options.HouseAdContentId))
                AddAllocation(run, null, screenId, [_options.HouseAdContentId], today, cursor, totalEnd, 0);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private void AddAllocation(
        ScheduleRun run,
        Advertisement? advertisement,
        string screenId,
        string[] contentIds,
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        long plays)
    {
        foreach (var contentId in contentIds)
            db.AdScreenAllocations.Add(new AdScreenAllocation
            {
                RunId = run.Id,
                AdvertisementId = advertisement?.Id,
                ScreenId = screenId,
                ContentId = contentId,
                PlayDate = date,
                StartTime = start,
                EndTime = end,
                PlannedPlays = plays,
                Status = "Planned",
                CreatedAtUtc = DateTime.UtcNow
            });
    }

    private async Task DispatchBatchAsync(AdScreenAllocation[] allocations, CancellationToken cancellationToken)
    {
        var first = allocations[0];
        var schedule = new
        {
            content = new[] { new { type = "creative", id = first.ContentId } },
            screen_ids = allocations.Select(item => item.ScreenId).Distinct(StringComparer.Ordinal).ToArray(),
            start_date = first.PlayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            end_date = first.PlayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            start_time = first.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            end_time = first.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            mode = "add"
        };

        try
        {
            await intelisa.ScheduleAsync(schedule, dryRun: true, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            foreach (var allocation in allocations)
            {
                allocation.Status = "Failed";
                allocation.LastError = "Intelisa dry-run validation failed; no schedule was submitted.";
                allocation.UpdatedAtUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);
            logger.LogError(exception, "Intelisa schedule preview failed for {ScreenCount} screens on {Date}.", schedule.screen_ids.Length, first.PlayDate);
            return;
        }

        if (!await TryMarkSubmittingAsync(allocations, cancellationToken))
            return;

        var status = "Sent";
        string? error = null;
        try
        {
            await intelisa.ScheduleAsync(schedule, dryRun: false, cancellationToken);
        }
        catch (IntelisaRequestException exception)
        {
            status = exception.OutcomeMayBeUnknown ? "Unconfirmed" : "Failed";
            error = exception.OutcomeMayBeUnknown
                ? "Intelisa may have applied this schedule; reconcile manually before retrying."
                : $"Intelisa rejected the schedule (HTTP {(int)exception.StatusCode}).";
            logger.LogError(exception, "Intelisa schedule write {Status} for {ScreenCount} screens.", status, schedule.screen_ids.Length);
        }
        catch (HttpRequestException exception)
        {
            status = "Unconfirmed";
            error = "The Intelisa write outcome is unknown; reconcile manually before retrying.";
            logger.LogError(exception, "Intelisa schedule write is unconfirmed for {ScreenCount} screens.", schedule.screen_ids.Length);
        }

        foreach (var allocation in allocations)
        {
            allocation.Status = status;
            allocation.LastError = error;
            allocation.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> TryMarkSubmittingAsync(
        IReadOnlyCollection<AdScreenAllocation> allocations,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var adId = allocations.First().AdvertisementId;
        if (adId.HasValue)
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SELECT [Id] FROM [dbo].[Advertisements] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = @id;";
            command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@id", adId.Value));
            var lockedId = await command.ExecuteScalarAsync(cancellationToken);
            var ad = lockedId is null
                ? null
                : await db.Advertisements.SingleOrDefaultAsync(item => item.Id == adId.Value, cancellationToken);
            if (ad is not null)
            {
                await db.Entry(ad).ReloadAsync(cancellationToken);
                var payment = db.Entry(ad).Reference(item => item.Payment);
                await payment.LoadAsync(cancellationToken);
                if (payment.CurrentValue is not null)
                    await db.Entry(payment.CurrentValue).ReloadAsync(cancellationToken);
            }

            var first = allocations.First();
            var currentlyEligible = ad is not null &&
                ad.Status is AdvertisementStatus.Active or AdvertisementStatus.Started &&
                ad.Payment?.Status == PaymentStatus.Completed && ad.PaidAmount >= ad.ApprovalAmount &&
                ad.ReadyForScheduling && ad.ContentId == first.ContentId && ad.ActualPlays < ad.TargetPlays &&
                DateOnly.FromDateTime(ad.StartDate) <= first.PlayDate && DateOnly.FromDateTime(ad.EndDate) >= first.PlayDate &&
                IsSetupReadyInTime(ad, DateOnly.FromDateTime(ad.StartDate)) &&
                allocations.All(item => _options.TargetAreaScreenIds.TryGetValue(ad.TargetArea, out var ids) && ids.Contains(item.ScreenId, StringComparer.Ordinal));
            if (!currentlyEligible)
            {
                foreach (var allocation in allocations)
                {
                    allocation.Status = "Failed";
                    allocation.LastError = "Campaign eligibility changed before the schedule write; no Intelisa schedule was submitted.";
                    allocation.UpdatedAtUtc = DateTime.UtcNow;
                }
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                logger.LogWarning("Skipped schedule write for advertisement {AdvertisementId} because it changed before dispatch.", adId);
                return false;
            }
        }

        foreach (var allocation in allocations)
        {
            allocation.Status = "Submitting";
            allocation.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private bool IsSetupReadyInTime(Advertisement ad, DateOnly startDate)
    {
        if (CampaignSetup.GetValidationMessage(ad, DateTimeOffset.UtcNow) is not null ||
            ad.ReadyForSchedulingAtUtc is null || ad.Payment?.ProcessedAt is null)
            return false;
        var readyDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(ad.ReadyForSchedulingAtUtc.Value), IndiaTimeZone));
        var paymentDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(ad.Payment.ProcessedAt.Value), IndiaTimeZone));
        return readyDate <= startDate && paymentDate <= startDate;
    }

    private async Task<long?> GetProvisionalPlaysAsync(
        Advertisement ad,
        DateOnly startDate,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        var firstUnreportedDay = ad.LastReportedDay?.AddDays(1) ?? startDate;
        if (firstUnreportedDay < startDate)
            firstUnreportedDay = startDate;
        if (firstUnreportedDay > throughDate)
            return 0;

        var rows = await db.AdScreenAllocations
            .Where(item => item.AdvertisementId == ad.Id && item.PlayDate >= firstUnreportedDay && item.PlayDate <= throughDate &&
                           (item.Status == "Sent" || item.Status == "Unconfirmed"))
            .ToListAsync(cancellationToken);
        var byDate = rows.GroupBy(item => item.PlayDate).ToDictionary(group => group.Key, group => group.Sum(item => item.PlannedPlays));
        var provisional = 0L;
        for (var date = firstUnreportedDay; date <= throughDate; date = date.AddDays(1))
        {
            if (!byDate.TryGetValue(date, out var plays))
                return null;
            provisional = checked(provisional + plays);
        }
        return provisional;
    }

    private bool IsRecentlyAvailable(IntelisaScreen screen)
    {
        if (string.Equals(screen.Status, "ON", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(screen.LastDataRecorded) ||
            !DateTime.TryParseExact(screen.LastDataRecorded, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var lastSeenIst))
            return false;
        var lastSeenUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(lastSeenIst, DateTimeKind.Unspecified), IndiaTimeZone);
        var age = DateTime.UtcNow - lastSeenUtc;
        return age >= TimeSpan.Zero && age <= TimeSpan.FromHours(24);
    }

    private async Task DispatchLockAsync(string resource, bool acquire, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = acquire
            ? "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @result;"
            : "EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner='Session';";
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@resource", resource));
        var result = acquire ? Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) : 0;
        if (acquire && result < 0)
            throw new InvalidOperationException("Another daily scheduler run is already in progress.");
    }

    private Task AcquireSessionLockAsync(CancellationToken cancellationToken) => DispatchLockAsync("daily-scheduler", true, cancellationToken);
    private Task ReleaseSessionLockAsync(CancellationToken cancellationToken) => DispatchLockAsync("daily-scheduler", false, cancellationToken);

    private void ValidateOptions()
    {
        var start = ParseTime(_options.OperatingWindowStartTime);
        var end = ParseTime(_options.OperatingWindowEndTime);
        if (end <= start || (end - start).TotalHours != _options.OperatingHoursPerDay)
            throw new InvalidOperationException("Scheduling operating window must have the configured number of hours.");
        if (!_options.MinuteScheduling && (start.Minute != 0 || end.Minute != 0))
            throw new InvalidOperationException("Scheduling operating window must use whole hours when minute scheduling is disabled.");
        if (string.IsNullOrWhiteSpace(_options.HouseAdContentId))
            throw new InvalidOperationException("Scheduling:HouseAdContentId is required as the commercial inventory fallback.");
        if (_options.LoopSeconds <= 0 || _options.ReservedSharePercent is < 0 or > 90)
            throw new InvalidOperationException("Scheduling loop and reserved-share settings are invalid.");
    }

    private static DateOnly ParseDate(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static TimeOnly ParseTime(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static TimeZoneInfo FindIndiaTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
    }
}
