using System.Security.Claims;
using Adrover.Application.DTOs;
using Adrover.Data.Context;
using Adrover.Domain.Entities;
using Adrover.Domain.Exceptions;
using Adrover.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Adrover.Api.Controllers;

[ApiController]
[Route("api/ClientDashboard")]
[Authorize(Roles = "Client")]
public class ClientDashboardController(AdRoverDbContext db, IFileStorageService storage) : ControllerBase
{
    private Guid ClientId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new AuthenticationException("Please sign in again.");

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var clientId = ClientId;
        var today = DateTime.UtcNow.Date;
        var firstDay = today.AddDays(-13);
        var ads = db.Advertisements.AsNoTracking().Where(a => a.ClientId == clientId);
        var recentPlays = db.AdPlays.AsNoTracking()
            .Where(p => p.Advertisement!.ClientId == clientId && p.PlayedAt >= firstDay && p.PlayedAt < today.AddDays(1));
        var grouped = await recentPlays.GroupBy(p => p.PlayedAt.Date)
            .Select(g => new { Date = g.Key, Plays = g.LongCount() }).ToListAsync(cancellationToken);
        var data = new
        {
            TotalAdvertisements = await ads.CountAsync(cancellationToken),
            ActiveAdvertisements = await ads.CountAsync(a => a.Status == AdvertisementStatus.Active, cancellationToken),
            PendingAdvertisements = await ads.CountAsync(a => a.Status == AdvertisementStatus.PendingApproval, cancellationToken),
            TotalPlays = await ads.SumAsync(a => a.ActualPlays, cancellationToken),
            ActiveCabs = await recentPlays.Where(p => p.VehicleNumber != "").Select(p => p.VehicleNumber).Distinct().CountAsync(cancellationToken),
            DailyPlays = Enumerable.Range(0, 14).Select(offset => new
            {
                Date = firstDay.AddDays(offset).ToString("yyyy-MM-dd"),
                Plays = grouped.SingleOrDefault(g => g.Date == firstDay.AddDays(offset))?.Plays ?? 0L
            }).ToArray()
        };
        return Ok(new ApiResponse<object> { Success = true, Data = data });
    }

    [HttpGet("campaigns/{id:guid}/playback")]
    public async Task<IActionResult> Playback(Guid id, [FromQuery] DateTime? date, [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        await Find(id, cancellationToken);
        if (pageNumber < 1 || pageSize < 1 || pageSize > 100 || (long)(pageNumber - 1) * pageSize > int.MaxValue)
            throw new BusinessLogicException("Choose a valid page.");
        var day = (date ?? DateTime.UtcNow).Date;
        if (day > DateTime.UtcNow.Date || day == DateTime.MaxValue.Date)
            throw new BusinessLogicException("Choose today or an earlier date.");
        var query = db.AdPlays.AsNoTracking().Where(p => p.AdvertisementId == id && p.PlayedAt >= day && p.PlayedAt < day.AddDays(1));
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(p => p.PlayedAt).ThenBy(p => p.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(p => new { p.Id, p.VehicleNumber, p.Latitude, p.Longitude, p.DurationSeconds, p.PlayedAt })
            .ToListAsync(cancellationToken);
        return Ok(new ApiResponse<object> { Success = true, Data = new { Items = items, Total = total } });
    }

    [HttpGet("campaigns/{id:guid}/creative")]
    public async Task<IActionResult> Creative(Guid id, CancellationToken cancellationToken)
    {
        var ad = await Find(id, cancellationToken);
        var videoUrl = await PreviewUrl(ad.VideoUrl, id);
        var imageUrl = await PreviewUrl(ad.ImageUrl, id);
        return Ok(new ApiResponse<object> { Success = true, Data = new { VideoUrl = videoUrl, ImageUrl = imageUrl } });
    }

    private async Task<Advertisement> Find(Guid id, CancellationToken cancellationToken) =>
        await db.Advertisements.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.ClientId == ClientId, cancellationToken)
        ?? throw new ResourceNotFoundException("Campaign not found.");

    private async Task<string?> PreviewUrl(string? value, Guid id)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new BusinessLogicException("This creative cannot be previewed. Please contact support.");
        var prefix = $"/advertisements/ads/{id}/";
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
            throw new BusinessLogicException("This creative cannot be previewed. Please contact support.");
        return await storage.GenerateSasUrlAsync("advertisements", path["/advertisements/".Length..], TimeSpan.FromMinutes(15));
    }
}
