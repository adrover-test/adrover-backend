using System.Security.Claims;
using System.Text.Json;
using Adrover.Application.DTOs;
using Adrover.Application.Services;
using Adrover.Backend.Services;
using Adrover.Data.Context;
using Adrover.Domain.Entities;
using Adrover.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Adrover.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "SuperAdminOnly")]
public sealed class AdminRegistrationFollowUpsController(
    AdRoverDbContext db,
    InquiryService messages,
    ILogger<AdminRegistrationFollowUpsController> logger) : ControllerBase
{
    private const string HistoryEntity = "ClientRegistrationFollowUp";

    [HttpGet("unfinished-registrations")]
    public async Task<IActionResult> List(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        if (pageNumber < 1 || pageSize < 1 || pageSize > 100
            || (long)(pageNumber - 1) * pageSize > int.MaxValue)
            throw new BusinessLogicException("Choose a valid page number and page size (1-100).");
        Response.Headers.CacheControl = "no-store";
        var unpaid = db.Advertisements.AsNoTracking().Where(a =>
            a.RegistrationProgress != null && a.Status == AdvertisementStatus.Draft
            && (a.ApprovalAmount == 0 || a.PaidAmount < a.ApprovalAmount)
            && !db.Payments.Any(p => p.AdvertisementId == a.Id && p.Status == PaymentStatus.Completed));
        var clients = db.Users.AsNoTracking().Where(u =>
            u.Role == UserRole.Client && unpaid.Any(a => a.ClientId == u.Id));
        var total = await clients.CountAsync(ct);
        var page = await clients.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(u => new { clientId = u.Id, name = u.FullName, u.Email, u.Phone })
            .ToListAsync(ct);
        var ids = page.Select(u => u.clientId).ToArray();
        var drafts = await unpaid.Where(a => ids.Contains(a.ClientId))
            .OrderByDescending(a => a.SubmittedAt).ToListAsync(ct);
        return Ok(new
        {
            items = page.Select(client => new
            {
                client.clientId, client.name, client.Email, client.Phone,
                drafts = drafts.Where(a => a.ClientId == client.clientId).Select(a =>
                {
                    var progress = RegistrationDraftWorkflow.ReadProgress(a);
                    decimal? amount = a.ApprovalAmount > 0 ? a.ApprovalAmount :
                        int.TryParse(progress.Days, out var days) && days >= 7 && days <= 3650
                        && int.TryParse(progress.DailyPlays, out var plays) && plays >= 1000 && plays <= 10000000
                            ? (decimal)days * plays * 0.30m : null;
                    return new
                    {
                        a.Id, a.Name, a.Brand, a.TargetArea, step = progress.Step,
                        a.SubmittedAt, a.UpdatedAt, amount, a.PaidAmount,
                        balance = amount.HasValue ? Math.Max(0, amount.Value - a.PaidAmount) : (decimal?)null,
                        hasVideo = !string.IsNullOrWhiteSpace(a.VideoUrl),
                        hasBanner = !string.IsNullOrWhiteSpace(a.ImageUrl)
                    };
                }).ToArray()
            }).ToArray(),
            total, pageNumber, pageSize
        });
    }

    [HttpGet("clients/{clientId:guid}/follow-ups")]
    public async Task<IActionResult> History(Guid clientId, CancellationToken ct)
    {
        await FindClient(clientId, ct);
        Response.Headers.CacheControl = "no-store";
        var entityId = clientId.ToString();
        var entries = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == HistoryEntity && a.EntityId == entityId)
            .OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync(ct);
        return Ok(entries.Select(ReadEntry).ToArray());
    }

    [HttpPost("clients/{clientId:guid}/follow-ups")]
    [EnableRateLimiting("client-follow-up")]
    public async Task<IActionResult> FollowUp(Guid clientId, ClientFollowUpRequest request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.Message)
            || (request.Channel == "email" && (string.IsNullOrWhiteSpace(request.Subject)
                || request.Subject.Contains('\r') || request.Subject.Contains('\n')))
            || (request.Channel == "sms" && request.Message.Length > 500))
            throw new BusinessLogicException("Provide a request ID, a message, an email subject, and no more than 500 characters for SMS.");
        var client = await FindClient(clientId, ct);
        var adminId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)
            ? actor : throw new AuthenticationException("Sign in again to follow up a client.");
        var previous = await db.AuditLogs.AsNoTracking().SingleOrDefaultAsync(a => a.Id == request.RequestId, ct);
        if (previous != null)
        {
            if (previous.EntityName != HistoryEntity || previous.EntityId != clientId.ToString()
                || previous.UserId != adminId)
                throw new BusinessLogicException("This follow-up request ID is already in use.");
            var saved = ReadEntry(previous);
            if (saved.Channel != request.Channel || saved.Message != request.Message.Trim()
                || saved.Subject != request.Subject.Trim() || saved.NextFollowUpAt != request.NextFollowUpAt)
                throw new BusinessLogicException("Use a new request ID for a different follow-up.");
            return Ok(saved);
        }
        if (request.NextFollowUpAt.HasValue && request.NextFollowUpAt < DateTimeOffset.UtcNow)
            throw new BusinessLogicException("Choose a future follow-up date.");

        var entry = new ClientFollowUpDto
        {
            Id = request.RequestId, Channel = request.Channel,
            Subject = request.Subject.Trim(), Message = request.Message.Trim(),
            Recipient = request.Channel == "email" ? client.Email : request.Channel == "sms" ? client.Phone : "",
            Status = request.Channel == "note" ? "Saved" : "Pending",
            By = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? adminId.ToString(),
            CreatedAt = DateTime.UtcNow, NextFollowUpAt = request.NextFollowUpAt?.ToUniversalTime()
        };
        var audit = new AuditLog
        {
            Id = entry.Id, UserId = adminId, Action = "Client follow-up",
            EntityName = HistoryEntity, EntityId = clientId.ToString(),
            NewValues = JsonSerializer.Serialize(entry), CreatedAt = entry.CreatedAt
        };
        db.AuditLogs.Add(audit);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Could not persist follow-up {FollowUpId}; no message was sent.", entry.Id);
            return Conflict(new { message = "Could not save the follow-up request. Reload its history before retrying." });
        }
        if (request.Channel != "note")
        {
            var result = await messages.SendClientMessageAsync(
                entry.Channel, entry.Recipient, entry.Subject, entry.Message);
            entry.Status = result.Status;
            entry.Error = result.Error;
            audit.NewValues = JsonSerializer.Serialize(entry);
            // Persist delivery outcome even if the browser disconnects after the provider request.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        logger.LogInformation("Client follow-up {FollowUpId}: {Channel}, {Status}, by {AdminId}",
            entry.Id, entry.Channel, entry.Status, adminId);
        return Ok(entry);
    }

    private async Task<User> FindClient(Guid id, CancellationToken ct) =>
        await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id && u.Role == UserRole.Client, ct)
        ?? throw new ResourceNotFoundException("Client not found.");

    private static ClientFollowUpDto ReadEntry(AuditLog entry) =>
        JsonSerializer.Deserialize<ClientFollowUpDto>(entry.NewValues
            ?? throw new InvalidOperationException("Client follow-up history is missing."))
        ?? throw new InvalidOperationException("Client follow-up history is invalid.");
}
