using Adrover.Data.Context;
using Adrover.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Adrover.Application.Services;

public static class ContentProcessingQueue
{
    public static async Task EnqueueIfReadyAsync(AdRoverDbContext db, Advertisement ad, Payment? payment)
    {
        if (payment?.Status != PaymentStatus.Completed || string.IsNullOrWhiteSpace(ad.VideoUrl) || ad.ContentState != "NotSubmitted")
            return;

        if (!Uri.TryCreate(ad.VideoUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("The uploaded video does not have a valid storage URL.");
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        var marker = $"/advertisements/ads/{ad.Id}/";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0 || index + marker.Length >= path.Length)
            throw new InvalidOperationException("The uploaded video is not in the expected advertisement storage path.");
        var blobPath = path[(index + "/advertisements/".Length)..];
        var latestVersion = await db.ContentOutbox.Where(item => item.AdvertisementId == ad.Id)
            .Select(item => (int?)item.AssetVersion).MaxAsync() ?? 0;
        db.ContentOutbox.Add(ContentOutbox.Create(ad.Id, checked(latestVersion + 1), blobPath, payment.Id, DateTime.UtcNow));
        ad.ContentState = "Queued";
        ad.ReadyForScheduling = false;
        ad.ReadyForSchedulingAtUtc = null;
    }
}
