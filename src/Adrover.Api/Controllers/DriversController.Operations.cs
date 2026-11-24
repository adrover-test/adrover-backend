using System.Data;
using System.Globalization;
using Adrover.Application.DTOs.Driver;
using Adrover.Domain.Entities;
using Adrover.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Adrover.Api.Controllers;

public sealed partial class DriversController
{
    private const string ImageContainer = "driver-private";

    private static DateTime UtcTimestamp(DateTime value)
    {
        if (value == default || value.Kind == DateTimeKind.Unspecified)
            throw new BusinessLogicException("Provide ISO timestamps with Z or a timezone offset.");
        return value.ToUniversalTime();
    }

    private static DateTime ParseDate(string? value, DateTime fallback)
    {
        if (value == null) return fallback;
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
            throw new BusinessLogicException("Enter dates in yyyy-MM-dd format.");
        return date;
    }

    private static DateTime PeriodStart(string period)
    {
        var today = DateTime.UtcNow.Date;
        return period switch
        {
            "weekly" => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
            "monthly" => new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => throw new BusinessLogicException("Choose weekly or monthly.")
        };
    }

    private async Task<string?> ImageUrlAsync(string? blob) => blob == null ? null
        : await files.GenerateSasUrlAsync(ImageContainer, blob, TimeSpan.FromMinutes(15));

    private async Task<string> UploadImageAsync(IFormFile image, string prefix, CancellationToken ct)
    {
        if (image.Length is <= 0 or > 5 * 1024 * 1024)
            throw new BusinessLogicException("Upload a JPEG or PNG image no larger than 5 MB.");
        await using var input = image.OpenReadStream();
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var png = bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var jpeg = bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
        if ((!png || image.ContentType != "image/png") && (!jpeg || image.ContentType != "image/jpeg"))
            throw new BusinessLogicException("The file must contain a JPEG or PNG image.");
        var blob = $"{prefix}/{Guid.NewGuid()}.{(png ? "png" : "jpg")}";
        buffer.Position = 0;
        await files.UploadFileAsync(ImageContainer, blob, buffer);
        return blob;
    }

    private async Task<object> BuildVehicleAsync(Driver driver, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var statuses = await db.DriverAssignments.AsNoTracking()
            .Where(a => a.DriverId == driver.Id && a.ScheduledAt >= today && a.ScheduledAt < today.AddDays(1)
                && a.Advertisement.Status == AdvertisementStatus.Active)
            .Select(a => a.Status).ToListAsync(ct);
        return new
        {
            id = driver.Id,
            registrationNumber = driver.VehicleNumber,
            model = driver.VehicleModel,
            type = "Cab",
            screenId = driver.ScreenId,
            tabletStatus = string.IsNullOrEmpty(driver.ScreenId) ? "not_assigned" : "unknown",
            approvalStatus = "approved",
            assignmentStatus = driver.Status == "Maintenance" ? "paused"
                : statuses.Contains("assigned") || driver.Status == "Assigned" ? "assigned"
                : statuses.Contains("paused") ? "paused" : "not_assigned",
            lastSyncAt = (string?)null
        };
    }

    private sealed record TodayAdvertisement(Guid Id, string Name, string AdvertiserName,
        string? ThumbnailUrl, DateTime ScheduledAt, int DurationSeconds, string Status,
        long PlaysCompleted, long PlaysRemaining);

    private async Task<List<TodayAdvertisement>> TodayAdsAsync(Guid driverId, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var now = DateTime.UtcNow;
        var assignments = await db.DriverAssignments.AsNoTracking()
            .Where(a => a.DriverId == driverId && a.ScheduledAt >= today && a.ScheduledAt < today.AddDays(1)
                && a.Advertisement.Status == AdvertisementStatus.Active)
            .OrderBy(a => a.ScheduledAt)
            .Select(a => new
            {
                a.Id, a.Advertisement.Name, a.Advertisement.Brand,
                a.Advertisement.ImageUrl, a.ScheduledAt, a.Advertisement.VideoDurationSeconds,
                a.TargetPlays, a.Status, DriverStatus = a.Driver.Status,
                Plays = db.DriverPlaybacks.Where(p => p.AssignmentId == a.Id).Sum(p => (long?)p.Plays) ?? 0
            }).ToListAsync(ct);
        return assignments.Select(a => new TodayAdvertisement(a.Id, a.Name, a.Brand, a.ImageUrl,
            a.ScheduledAt, (int)a.VideoDurationSeconds,
            a.Plays >= a.TargetPlays || a.Status == "completed" ? "completed"
                : a.Status == "paused" || a.DriverStatus == "Maintenance" ? "paused"
                : a.ScheduledAt > now || a.Plays == 0 ? "upcoming" : "playing",
            a.Plays, Math.Max(0, a.TargetPlays - a.Plays))).ToList();
    }

    private async Task<object> BuildEarningsAsync(Guid driverId, string period, CancellationToken ct)
    {
        var start = PeriodStart(period);
        var month = PeriodStart("monthly");
        var plays = db.DriverPlaybacks.AsNoTracking().Where(p => p.Assignment.DriverId == driverId);
        var total = await plays.SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var payouts = db.DriverPayouts.AsNoTracking().Where(p => p.DriverId == driverId);
        var released = await payouts.Where(p => p.Status == "released").SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var processing = await payouts.Where(p => p.Status == "processing").SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var perAd = await plays.Where(p => p.PlayedAt >= start)
            .GroupBy(p => new { p.Assignment.AdvertisementId, p.Assignment.Advertisement.Name })
            .Select(g => new { adId = g.Key.AdvertisementId, adName = g.Key.Name, plays = g.Sum(p => p.Plays), amount = g.Sum(p => p.Amount) })
            .ToListAsync(ct);
        var next = await payouts.Where(p => p.Status == "pending" || p.Status == "processing")
            .OrderBy(p => p.ScheduledAt).Select(p => new { date = p.ScheduledAt, amount = p.Amount }).FirstOrDefaultAsync(ct);
        return new
        {
            total,
            thisMonth = await plays.Where(p => p.PlayedAt >= month).SumAsync(p => (decimal?)p.Amount, ct) ?? 0m,
            pending = Math.Max(0m, total - released - processing),
            processing, released, nextPayout = next, perAd
        };
    }

    private async Task<object> BuildDashboardAsync(Driver driver, CancellationToken ct)
    {
        var month = PeriodStart("monthly");
        var today = DateTime.UtcNow.Date;
        var plays = db.DriverPlaybacks.AsNoTracking().Where(p => p.Assignment.DriverId == driver.Id);
        var monthPlays = plays.Where(p => p.PlayedAt >= month);
        var earned = await plays.SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var payouts = db.DriverPayouts.AsNoTracking().Where(p => p.DriverId == driver.Id);
        var reserved = await payouts.Where(p => p.Status == "released" || p.Status == "processing")
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var ads = await TodayAdsAsync(driver.Id, ct);
        var next = ads.FirstOrDefault(a => a.Status == "upcoming" && a.PlaysRemaining > 0);
        return new
        {
            month = new
            {
                adsPlayed = await monthPlays.SumAsync(p => (long?)p.Plays, ct) ?? 0L,
                amountEarned = await monthPlays.SumAsync(p => (decimal?)p.Amount, ct) ?? 0m,
                pendingAmount = Math.Max(0m, earned - reserved),
                releasedAmount = await payouts.Where(p => p.Status == "released" && p.PaidAt >= month)
                    .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m,
                workingDays = await monthPlays.Select(p => p.PlayedAt.Date).Distinct().CountAsync(ct),
                activeCampaigns = await db.DriverAssignments.Where(a => a.DriverId == driver.Id
                    && a.ScheduledAt >= month && a.ScheduledAt < today.AddDays(1)
                    && a.Status == "assigned" && a.Advertisement.Status == AdvertisementStatus.Active)
                    .Select(a => a.AdvertisementId).Distinct().CountAsync(ct)
            },
            today = new
            {
                scheduled = ads.Sum(a => a.PlaysCompleted + a.PlaysRemaining),
                played = ads.Sum(a => a.PlaysCompleted),
                remaining = ads.Sum(a => a.PlaysRemaining),
                next = next == null ? null : new { name = next.Name, scheduledAt = next.ScheduledAt }
            },
            device = new { status = string.IsNullOrEmpty(driver.ScreenId) ? "not_assigned" : "unknown", lastSyncAt = (string?)null }
        };
    }

    private static object IssueSummary(DriverIssue issue) => new
    {
        issue.Id, ticketId = $"DRV-{issue.Id:N}", issue.Category, issue.Description,
        issue.Status, issue.CreatedAt, issue.UpdatedAt, issue.AdminResponse
    };

    private async Task<object> IssueDetailAsync(DriverIssue issue, CancellationToken ct)
    {
        var advertisementName = issue.AdvertisementId == null ? null
            : await db.Advertisements.Where(a => a.Id == issue.AdvertisementId).Select(a => a.Name).SingleOrDefaultAsync(ct);
        return new
        {
            issue.Id, ticketId = $"DRV-{issue.Id:N}", issue.Category, issue.Description,
            issue.Status, issue.CreatedAt, issue.UpdatedAt, issue.AdminResponse,
            issue.Language, advertisementName, issue.VehicleRegistration,
            attachmentUrl = await ImageUrlAsync(issue.AttachmentBlobName),
            canConfirm = issue.Status == "resolved" && !issue.Confirmed,
            canReopen = (issue.Status == "resolved" || issue.Status == "rejected") && !issue.Confirmed,
            history = issue.History.OrderBy(h => h.At).Select(h => new { h.Status, h.Note, h.By, h.At })
        };
    }

    private void Notify(Guid driverId, string title, string message, string type)
    {
        db.DriverNotifications.Add(new DriverNotification
        {
            Id = Guid.NewGuid(), DriverId = driverId, Title = title, Message = message,
            Type = type, CreatedAt = DateTime.UtcNow
        });
    }

    private static void AddHistory(DriverIssue issue, string by, string? note)
    {
        issue.UpdatedAt = DateTime.UtcNow;
        issue.History.Add(new DriverIssueHistory
        {
            Id = Guid.NewGuid(), IssueId = issue.Id, Status = issue.Status, Note = note,
            By = by, At = issue.UpdatedAt
        });
    }

    private async Task<IActionResult> CreateIssueAsync(Driver driver, DriverIssueRequest request, IFormFile? attachment, CancellationToken ct)
    {
        request.OccurredAt = UtcTimestamp(request.OccurredAt);
        if (!Guid.TryParse(Request.Headers["Idempotency-Key"], out var key) || key == Guid.Empty)
            throw new BusinessLogicException("Provide a valid Idempotency-Key.");
        Guid? advertisementId = null;
        if (request.AdvertisementId.HasValue)
        {
            var assignment = await db.DriverAssignments.AsNoTracking()
                .SingleOrDefaultAsync(a => a.Id == request.AdvertisementId && a.DriverId == driver.Id, ct);
            if (assignment == null)
                throw new BusinessLogicException("Choose a campaign assigned to you.");
            advertisementId = assignment.AdvertisementId;
        }
        var existing = await db.DriverIssues.AsNoTracking()
            .SingleOrDefaultAsync(i => i.DriverId == driver.Id && i.RequestId == key, ct);
        if (existing != null)
        {
            if (existing.Description != request.Description.Trim() || existing.Category != request.Category
                || existing.AdvertisementId != advertisementId || existing.ContactMobile != request.ContactMobile
                || existing.Language != request.Language || existing.OccurredAt != request.OccurredAt
                || (existing.AttachmentBlobName != null) != (attachment != null))
                return Conflict(new { message = "This request ID was already used for another issue." });
            return Ok(new { existing.Id, ticketId = $"DRV-{existing.Id:N}" });
        }
        if (request.Description.Trim().Length < 10 || request.OccurredAt == default || request.OccurredAt > DateTime.UtcNow.AddMinutes(5))
            throw new BusinessLogicException("Enter a description of at least 10 characters and a valid occurrence time.");
        if (request.VehicleId.HasValue && request.VehicleId != driver.Id)
            throw new BusinessLogicException("Choose your own vehicle.");
        var now = DateTime.UtcNow;
        var issue = new DriverIssue
        {
            Id = Guid.NewGuid(), DriverId = driver.Id, RequestId = key,
            AdvertisementId = advertisementId, Category = request.Category, Description = request.Description.Trim(),
            Language = request.Language, ContactMobile = request.ContactMobile,
            VehicleRegistration = driver.VehicleNumber, OccurredAt = request.OccurredAt,
            CreatedAt = now, UpdatedAt = now,
            AttachmentBlobName = attachment == null ? null : await UploadImageAsync(attachment, $"issues/{driver.Id}", ct)
        };
        AddHistory(issue, "driver", "Issue submitted.");
        db.DriverIssues.Add(issue);
        await db.SaveChangesAsync(ct);
        return Ok(new { issue.Id, ticketId = $"DRV-{issue.Id:N}" });
    }

    private async Task<IActionResult> ChangeIssueAsync(Guid id, string? reason, CancellationToken ct)
    {
        var driver = await FindDriverAsync(ct);
        if (driver == null) return Unauthorized();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var issue = await db.DriverIssues.Include(i => i.History)
            .SingleOrDefaultAsync(i => i.Id == id && i.DriverId == driver.Id, ct);
        if (issue == null) return NotFound(new { message = "Issue not found." });
        if (issue.Confirmed || (reason == null ? issue.Status != "resolved" : issue.Status is not ("resolved" or "rejected")))
            throw new BusinessLogicException("This issue cannot be confirmed or reopened in its current state.");
        if (reason != null && reason.Trim().Length < 10)
            throw new BusinessLogicException("Enter a reopening reason of at least 10 characters.");
        if (reason == null) issue.Confirmed = true;
        else issue.Status = "submitted";
        AddHistory(issue, "driver", reason?.Trim() ?? "Driver confirmed resolution.");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }
}
