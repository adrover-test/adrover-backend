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

[ApiController]
[Route("api/Advertisements/drafts")]
[Authorize(Roles = "Client")]
public class RegistrationDraftsController : ControllerBase
{
    private readonly AdRoverDbContext _db;

    public RegistrationDraftsController(AdRoverDbContext db) => _db = db;

    private Guid ClientId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new AuthenticationException("Invalid user ID.");

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var ads = await _db.Advertisements.Where(a => a.ClientId == ClientId && a.RegistrationProgress != null)
            .OrderByDescending(a => a.SubmittedAt).ToListAsync();
        var ids = ads.Select(a => a.Id).ToList();
        var paidIds = await _db.Payments.Where(p => p.AdvertisementId.HasValue && ids.Contains(p.AdvertisementId.Value))
            .Select(p => p.AdvertisementId!.Value).Distinct().ToListAsync();
        return Ok(new ApiResponse<List<RegistrationDraftDto>>
        {
            Success = true,
            Data = ads.Select(a => Map(a, paidIds.Contains(a.Id))).ToList()
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var ad = await Find(id);
        return Result(ad, await _db.Payments.AnyAsync(p => p.AdvertisementId == id));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Save(Guid id, RegistrationDraftRequest request)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var clientId = ClientId;
        var ad = await _db.Advertisements.FromSqlInterpolated(
            $"SELECT * FROM [Advertisements] WITH (UPDLOCK) WHERE [Id] = {id} AND [ClientId] = {clientId}")
            .SingleOrDefaultAsync() ?? throw new ResourceNotFoundException("Registration draft not found.");
        if (CampaignSetup.HasStarted(ad))
            throw new BusinessLogicException("This advertisement has started playing and can no longer be edited.", "AD_PLAYBACK_STARTED");
        if (ad.RegistrationProgress == null || ad.Status != AdvertisementStatus.Draft)
            throw new BusinessLogicException("This campaign is no longer an editable draft.");
        if (await _db.Payments.AnyAsync(p => p.AdvertisementId == id))
            throw new BusinessLogicException("Checkout has started. The campaign plan is locked to prevent an incorrect charge.");
        if (request.Id != id)
            throw new BusinessLogicException("Draft IDs do not match.");
        ad.Name = request.Name.Trim();
        ad.Brand = request.Brand.Trim();
        ad.TargetArea = request.TargetArea.Trim();
        ad.VideoDurationSeconds = request.VideoDurationSeconds;
        ad.RegistrationProgress = JsonSerializer.Serialize(request);
        ad.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Result(ad, false);
    }

    private async Task<Advertisement> Find(Guid id) =>
        await _db.Advertisements.SingleOrDefaultAsync(a => a.Id == id && a.ClientId == ClientId && a.RegistrationProgress != null)
        ?? throw new ResourceNotFoundException("Registration draft not found.");

    private IActionResult Result(Advertisement ad, bool paymentStarted) =>
        Ok(new ApiResponse<RegistrationDraftDto> { Success = true, Data = Map(ad, paymentStarted) });

    private static RegistrationDraftDto Map(Advertisement ad, bool paymentStarted) => new()
    {
        Id = ad.Id,
        Progress = RegistrationDraftWorkflow.ReadProgress(ad),
        HasVideo = !string.IsNullOrEmpty(ad.VideoUrl),
        HasBanner = !string.IsNullOrEmpty(ad.ImageUrl),
        Status = ad.Status.ToString(),
        Amount = ad.ApprovalAmount,
        PaidAmount = ad.PaidAmount,
        PaymentStarted = paymentStarted,
        ContentState = ad.ContentState,
        ReadyForScheduling = ad.ReadyForScheduling,
        FirstPlayedAt = ad.FirstPlayedAt,
        FirstPlaybackDate = ad.FirstPlaybackDate,
        LastReportedDay = ad.LastReportedDay,
        IsEditable = !paymentStarted && ad.Status == AdvertisementStatus.Draft && !CampaignSetup.HasStarted(ad),
        SetupValidationMessage = CampaignSetup.GetValidationMessage(ad, DateTimeOffset.UtcNow)
    };
}
