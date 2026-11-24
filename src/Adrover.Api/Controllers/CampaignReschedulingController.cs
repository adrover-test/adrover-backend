using System.Security.Claims;
using System.Text.Json;
using Adrover.Application.DTOs;
using Adrover.Application.DTOs.Advertisement;
using Adrover.Application.Services;
using Adrover.Data.Context;
using Adrover.Domain.Entities;
using Adrover.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Adrover.Api.Controllers;

public sealed class RescheduleCampaignRequest
{
    public DateOnly StartDate { get; set; }
}

[ApiController]
[Route("api/Advertisements")]
[Authorize(Roles = "Client")]
public sealed class CampaignReschedulingController(AdRoverDbContext db, IAdvertisementService advertisements, TimeProvider clock) : ControllerBase
{
    [HttpPut("{id:guid}/reschedule")]
    public async Task<IActionResult> Reschedule(Guid id, [FromBody] RescheduleCampaignRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var clientId))
            throw new AuthenticationException("Please sign in again.");

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        if (request.StartDate <= today)
            throw new BusinessLogicException(CampaignSetup.StartDatePassedMessage, "AD_SETUP_INCOMPLETE_START_PASSED");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var ad = await db.Advertisements.FromSqlInterpolated(
            $"SELECT * FROM [Advertisements] WITH (UPDLOCK) WHERE [Id] = {id} AND [ClientId] = {clientId}")
            .SingleOrDefaultAsync(cancellationToken) ?? throw new ResourceNotFoundException("Advertisement not found.");

        if (CampaignSetup.HasStarted(ad))
            throw new BusinessLogicException("This advertisement has started playing and can no longer be edited.", "AD_PLAYBACK_STARTED");
        if (await db.AdScreenAllocations.AnyAsync(a => a.AdvertisementId == id &&
            (a.Status == "Submitting" || a.Status == "Sent" || a.Status == "Unconfirmed"), cancellationToken))
            throw new BusinessLogicException("This campaign has a submitted or unconfirmed provider schedule. Contact support before rescheduling.", "AD_SCHEDULE_SUBMITTED");
        if (ad.Status is AdvertisementStatus.Draft or AdvertisementStatus.Rejected or AdvertisementStatus.Cancelled or AdvertisementStatus.Completed)
            throw new BusinessLogicException("This campaign cannot be rescheduled in its current state.");
        if (CampaignSetup.GetValidationMessage(ad, now) == null)
            throw new BusinessLogicException("Rescheduling is available only when the start date passed before setup was complete.");

        var oldStart = DateOnly.FromDateTime(ad.StartDate);
        var oldEnd = DateOnly.FromDateTime(ad.EndDate);
        var duration = oldEnd.DayNumber - oldStart.DayNumber;
        if (duration < 0 || (long)request.StartDate.DayNumber + duration > DateOnly.MaxValue.DayNumber)
            throw new BusinessLogicException("The campaign dates are invalid.");

        ad.StartDate = request.StartDate.ToDateTime(TimeOnly.MinValue);
        ad.EndDate = request.StartDate.AddDays(duration).ToDateTime(TimeOnly.MinValue);
        if (ad.RegistrationProgress != null)
        {
            var progress = RegistrationDraftWorkflow.ReadProgress(ad);
            progress.StartDate = request.StartDate.ToString("yyyy-MM-dd");
            ad.RegistrationProgress = JsonSerializer.Serialize(progress);
        }
        ad.UpdatedAt = now.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new ApiResponse<AdvertisementDto>
        {
            Success = true,
            Data = await advertisements.GetAdvertisementAsync(id),
            Message = "Campaign rescheduled successfully."
        });
    }
}
