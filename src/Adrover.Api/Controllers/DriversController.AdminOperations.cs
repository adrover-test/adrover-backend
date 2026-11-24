using System.Data;
using Adrover.Application.DTOs.Driver;
using Adrover.Domain.Entities;
using Adrover.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Adrover.Api.Controllers;

public sealed partial class DriversController
{
    [HttpGet("{driverId:guid}/operations")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> GetOperations(Guid driverId, CancellationToken ct)
    {
        if (!await db.Drivers.AnyAsync(d => d.Id == driverId, ct))
            return NotFound(new { message = "Driver not found." });
        Response.Headers.CacheControl = "no-store";
        var assignments = await db.DriverAssignments.AsNoTracking().Where(a => a.DriverId == driverId)
            .OrderByDescending(a => a.ScheduledAt)
            .Select(a => new { a.Id, a.AdvertisementId, name = a.Advertisement.Name, a.ScheduledAt,
                a.TargetPlays, a.RatePerPlay, a.Status,
                playsCompleted = db.DriverPlaybacks.Where(p => p.AssignmentId == a.Id).Sum(p => (long?)p.Plays) ?? 0L })
            .ToListAsync(ct);
        var issues = await db.DriverIssues.AsNoTracking().Where(i => i.DriverId == driverId)
            .OrderByDescending(i => i.UpdatedAt).ToListAsync(ct);
        return Ok(new
        {
            campaigns = await db.Advertisements.AsNoTracking()
                .Where(a => a.Status == AdvertisementStatus.Active || a.Status == AdvertisementStatus.Approved)
                .OrderBy(a => a.Name).Select(a => new { a.Id, a.Name, a.StartDate, a.EndDate }).ToListAsync(ct),
            assignments,
            earnings = await BuildEarningsAsync(driverId, "monthly", ct),
            payouts = await db.DriverPayouts.AsNoTracking().Where(p => p.DriverId == driverId)
                .OrderByDescending(p => p.CreatedAt).ToListAsync(ct),
            issues = issues.Select(i => new { summary = IssueSummary(i), i.Confirmed })
        });
    }

    [HttpPost("{driverId:guid}/assignments")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> AssignCampaign(Guid driverId, DriverAssignmentRequest request, CancellationToken ct)
    {
        request.ScheduledAt = UtcTimestamp(request.ScheduledAt);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var driver = await db.Drivers.SingleOrDefaultAsync(d => d.Id == driverId && d.IsActive, ct);
        if (driver == null) return NotFound(new { message = "Active driver not found." });
        var ad = await db.Advertisements.SingleOrDefaultAsync(a => a.Id == request.AdvertisementId, ct);
        if (ad == null || ad.Status is not (AdvertisementStatus.Active or AdvertisementStatus.Approved))
            throw new BusinessLogicException("Choose an approved or active campaign.");
        if (driver.Status == "Maintenance" || request.ScheduledAt < DateTime.UtcNow.Date
            || request.ScheduledAt < ad.StartDate || request.ScheduledAt > ad.EndDate
            || request.TargetPlays < 1 || request.RatePerPlay <= 0
            || request.RatePerPlay != decimal.Round(request.RatePerPlay, 4))
            throw new BusinessLogicException("Choose an available driver, a schedule within campaign dates, a positive play target, and a positive rate with at most four decimals.");
        var allocated = await db.DriverAssignments.Where(a => a.AdvertisementId == ad.Id && a.Status != "completed")
            .Select(a => a.TargetPlays - (db.DriverPlaybacks.Where(p => p.AssignmentId == a.Id).Sum(p => (long?)p.Plays) ?? 0L))
            .SumAsync(ct);
        if (allocated + request.TargetPlays > ad.TargetPlays - ad.ActualPlays)
            throw new BusinessLogicException("Assigned plays would exceed the campaign target.");
        var assignment = new DriverAssignment
        {
            Id = Guid.NewGuid(), DriverId = driverId, AdvertisementId = ad.Id,
            ScheduledAt = request.ScheduledAt, TargetPlays = request.TargetPlays,
            RatePerPlay = request.RatePerPlay, CreatedAt = DateTime.UtcNow
        };
        db.DriverAssignments.Add(assignment);
        Notify(driverId, "Campaign assigned", $"{ad.Name} is scheduled for {request.ScheduledAt:yyyy-MM-dd HH:mm} UTC.", "campaign");
        await SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Driver assignment created: {AssignmentId} by {Admin}", assignment.Id, User.Identity?.Name);
        return Ok(new { assignment.Id });
    }

    [HttpPut("{driverId:guid}/assignments/{assignmentId:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> SetAssignmentStatus(Guid driverId, Guid assignmentId, DriverAssignmentStatusRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var assignment = await db.DriverAssignments.SingleOrDefaultAsync(a => a.Id == assignmentId && a.DriverId == driverId, ct);
        if (assignment == null) return NotFound(new { message = "Assignment not found." });
        if (assignment.Status == "completed" && request.Status != "completed")
            throw new BusinessLogicException("Completed assignments cannot be reopened.");
        assignment.Status = request.Status;
        Notify(driverId, "Campaign assignment updated", $"Assignment status: {request.Status}.", "campaign");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpPost("{driverId:guid}/assignments/{assignmentId:guid}/plays")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> RecordDriverPlays(Guid driverId, Guid assignmentId, DriverPlaybackRequest request, CancellationToken ct)
    {
        request.PlayedAt = UtcTimestamp(request.PlayedAt);
        if (request.RequestId == Guid.Empty || request.Plays <= 0)
            throw new BusinessLogicException("Provide a request ID and positive confirmed play count.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var assignment = await db.DriverAssignments.Include(a => a.Driver).Include(a => a.Advertisement)
            .SingleOrDefaultAsync(a => a.Id == assignmentId && a.DriverId == driverId, ct);
        if (assignment == null) return NotFound(new { message = "Assignment not found." });
        var existing = await db.DriverPlaybacks.SingleOrDefaultAsync(p => p.RequestId == request.RequestId, ct);
        if (existing != null)
            return existing.AssignmentId == assignmentId && existing.Plays == request.Plays && existing.PlayedAt == request.PlayedAt
                ? Ok(new { existing.Id }) : Conflict(new { message = "This request ID is already used for different playback." });
        if (!assignment.Driver.IsActive || assignment.Status != "assigned" || assignment.Driver.Status == "Maintenance"
            || assignment.Advertisement.Status != AdvertisementStatus.Active
            || request.PlayedAt < assignment.ScheduledAt || request.PlayedAt >= assignment.ScheduledAt.Date.AddDays(1)
            || request.PlayedAt > DateTime.UtcNow)
            throw new BusinessLogicException("Record confirmed plays within the assigned UTC day for an active campaign and driver.");
        var completed = await db.DriverPlaybacks.Where(p => p.AssignmentId == assignmentId).SumAsync(p => (long?)p.Plays, ct) ?? 0L;
        if (completed + request.Plays > assignment.TargetPlays
            || assignment.Advertisement.ActualPlays + request.Plays > assignment.Advertisement.TargetPlays)
            throw new BusinessLogicException("Confirmed plays exceed the assignment or campaign target.");
        var playback = new DriverPlayback
        {
            Id = Guid.NewGuid(), AssignmentId = assignmentId, RequestId = request.RequestId,
            Plays = request.Plays, Amount = request.Plays * assignment.RatePerPlay, PlayedAt = request.PlayedAt
        };
        db.DriverPlaybacks.Add(playback);
        db.AdPlays.Add(new AdPlay
        {
            Id = playback.Id, AdvertisementId = assignment.AdvertisementId,
            DriverPhone = assignment.Driver.Phone, VehicleNumber = assignment.Driver.VehicleNumber,
            TotalPlays = checked((int)request.Plays), PlayedAt = request.PlayedAt,
            DurationSeconds = (int)assignment.Advertisement.VideoDurationSeconds
        });
        assignment.Advertisement.ActualPlays += request.Plays;
        if (completed + request.Plays == assignment.TargetPlays) assignment.Status = "completed";
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Confirmed driver playback recorded: {PlaybackId} by {Admin}", playback.Id, User.Identity?.Name);
        return Ok(new { playback.Id });
    }

    [HttpPost("{driverId:guid}/payouts")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> CreateDriverPayout(Guid driverId, DriverPayoutRequest request, CancellationToken ct)
    {
        request.PeriodStart = UtcTimestamp(request.PeriodStart);
        request.PeriodEnd = UtcTimestamp(request.PeriodEnd);
        request.ScheduledAt = UtcTimestamp(request.ScheduledAt);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await db.Drivers.AnyAsync(d => d.Id == driverId && d.IsActive, ct))
            return NotFound(new { message = "Active driver not found." });
        if (request.TransactionId.Trim().Length == 0 || request.Amount != decimal.Round(request.Amount, 2)
            || request.PeriodStart == default || request.PeriodEnd < request.PeriodStart
            || request.PeriodEnd > DateTime.UtcNow || request.ScheduledAt == default)
            throw new BusinessLogicException("Enter a transaction ID, an amount with at most two decimals, and a valid earned period and payout date.");
        var earned = await db.DriverPlaybacks.Where(p => p.Assignment.DriverId == driverId)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var reserved = await db.DriverPayouts.Where(p => p.DriverId == driverId && p.Status != "failed")
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var periodEarned = await db.DriverPlaybacks.Where(p => p.Assignment.DriverId == driverId
            && p.PlayedAt >= request.PeriodStart && p.PlayedAt <= request.PeriodEnd)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var periodReserved = await db.DriverPayouts.Where(p => p.DriverId == driverId && p.Status != "failed"
            && p.PeriodStart <= request.PeriodEnd && p.PeriodEnd >= request.PeriodStart)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        if (request.Amount > earned - reserved || request.Amount > periodEarned - periodReserved)
            throw new BusinessLogicException("The payout exceeds unreserved earnings or earnings in the selected period.");
        var payout = new DriverPayout
        {
            Id = Guid.NewGuid(), DriverId = driverId, TransactionId = request.TransactionId.Trim(),
            Amount = request.Amount, PeriodStart = request.PeriodStart, PeriodEnd = request.PeriodEnd,
            ScheduledAt = request.ScheduledAt, CreatedAt = DateTime.UtcNow
        };
        db.DriverPayouts.Add(payout);
        Notify(driverId, "Payout scheduled", $"A payout of INR {payout.Amount:0.00} is scheduled.", "payment");
        await SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Driver payout scheduled: {PayoutId} by {Admin}", payout.Id, User.Identity?.Name);
        return Ok(new { payout.Id });
    }

    [HttpPut("{driverId:guid}/payouts/{payoutId:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> UpdateDriverPayout(Guid driverId, Guid payoutId, DriverPayoutStatusRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var payout = await db.DriverPayouts.SingleOrDefaultAsync(p => p.Id == payoutId && p.DriverId == driverId, ct);
        if (payout == null) return NotFound(new { message = "Payout not found." });
        if (payout.Status == request.Status) return NoContent();
        if (payout.Status is "released" or "failed" || request.Status == "pending")
            throw new BusinessLogicException("Released and failed payouts are final. Create a new record to retry a failed payout.");
        if (request.Status == "released" && string.IsNullOrWhiteSpace(request.Reference))
            throw new BusinessLogicException("Record the external payment reference before marking a payout released.");
        payout.Status = request.Status;
        payout.Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();
        payout.PaidAt = request.Status == "released" ? DateTime.UtcNow : null;
        Notify(driverId, "Payout updated", $"Payout {payout.TransactionId}: {payout.Status}.", "payment");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Driver payout status updated: {PayoutId} to {Status} by {Admin}", payout.Id, payout.Status, User.Identity?.Name);
        return NoContent();
    }

    [HttpPost("{driverId:guid}/notifications")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> SendDriverNotification(Guid driverId, DriverNotificationRequest request, CancellationToken ct)
    {
        if (!await db.Drivers.AnyAsync(d => d.Id == driverId, ct))
            return NotFound(new { message = "Driver not found." });
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Message))
            throw new BusinessLogicException("Enter a notification title and message.");
        Notify(driverId, request.Title.Trim(), request.Message.Trim(), "admin");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{driverId:guid}/issues/{issueId:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> GetDriverIssue(Guid driverId, Guid issueId, CancellationToken ct)
    {
        var issue = await db.DriverIssues.AsNoTracking().Include(i => i.History)
            .SingleOrDefaultAsync(i => i.Id == issueId && i.DriverId == driverId, ct);
        return issue == null ? NotFound(new { message = "Issue not found." }) : Ok(await IssueDetailAsync(issue, ct));
    }

    [HttpPut("{driverId:guid}/issues/{issueId:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> RespondToDriverIssue(Guid driverId, Guid issueId, DriverIssueResponseRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var issue = await db.DriverIssues.Include(i => i.History)
            .SingleOrDefaultAsync(i => i.Id == issueId && i.DriverId == driverId, ct);
        if (issue == null) return NotFound(new { message = "Issue not found." });
        if (issue.Confirmed || issue.Status is "resolved" or "rejected")
            throw new BusinessLogicException("Closed issues must be reopened by the driver before further updates.");
        if (string.IsNullOrWhiteSpace(request.Note))
            throw new BusinessLogicException("Enter a response for the driver.");
        issue.Status = request.Status;
        issue.AdminResponse = request.Note.Trim();
        AddHistory(issue, "admin", issue.AdminResponse);
        Notify(driverId, "Support ticket updated", $"Ticket DRV-{issue.Id:N}: {issue.Status}. Open the ticket to read the response.", "support");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }
}
