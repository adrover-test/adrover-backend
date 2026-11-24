using Adrover.Data.Context;
using Adrover.Domain.Entities;
using Adrover.Functions.Configuration;
using Adrover.Functions.Contracts;
using Adrover.Functions.Intelisa;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;

namespace Adrover.Functions.Services;

public enum ContentProcessingResult
{
    Complete,
    PollAgain
}

public sealed class ContentProcessor(
    AdRoverDbContext db,
    BlobServiceClient blobServiceClient,
    IntelisaClient intelisa,
    IConfiguration configuration,
    ILogger<ContentProcessor> logger)
{
    public async Task<ContentProcessingResult> ProcessAsync(ContentProcessingMessage message, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var lockAcquired = false;
        try
        {
            await AcquireContentLockAsync(message.AdvertisementId, cancellationToken);
            lockAcquired = true;
            return await ProcessCoreAsync(message, cancellationToken);
        }
        finally
        {
            if (lockAcquired)
                await ReleaseContentLockAsync(message.AdvertisementId);
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<ContentProcessingResult> ProcessCoreAsync(ContentProcessingMessage message, CancellationToken cancellationToken)
    {
        var advertisement = await db.Advertisements.Include(item => item.Payment)
            .SingleOrDefaultAsync(item => item.Id == message.AdvertisementId, cancellationToken);
        if (advertisement is null || advertisement.Status is AdvertisementStatus.Cancelled or AdvertisementStatus.Rejected)
            return ContentProcessingResult.Complete;

        if (message.AssetVersion != await GetCurrentAssetVersionAsync(message.AdvertisementId, cancellationToken))
        {
            logger.LogInformation("Ignoring stale content asset version {AssetVersion} for advertisement {AdvertisementId}.", message.AssetVersion, message.AdvertisementId);
            return ContentProcessingResult.Complete;
        }

        if (advertisement.ContentAssetVersion.HasValue && advertisement.ContentAssetVersion != message.AssetVersion)
        {
            advertisement.ContentId = null;
            advertisement.ContentAssetVersion = null;
            advertisement.ContentState = "Queued";
            advertisement.ReadyForScheduling = false;
            advertisement.ReadyForSchedulingAtUtc = null;
            advertisement.ContentSubmittedAtUtc = null;
            advertisement.ContentError = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        if (advertisement.ContentState == "Ready" && advertisement.ReadyForScheduling && !string.IsNullOrWhiteSpace(advertisement.ContentId))
            return ContentProcessingResult.Complete;
        if (advertisement.ContentState == "Failed")
            return ContentProcessingResult.Complete;
        if (advertisement.Payment?.Status != PaymentStatus.Completed ||
            (advertisement.PaidAmount < advertisement.ApprovalAmount && advertisement.ApprovalAmount > 0))
        {
            logger.LogWarning("Content processing deferred because payment is not completed for advertisement {AdvertisementId}.", message.AdvertisementId);
            return ContentProcessingResult.PollAgain;
        }

        if (string.IsNullOrWhiteSpace(advertisement.ContentId))
        {
            if (advertisement.ContentState == "Uploading" || advertisement.ContentState == "UploadUnconfirmed")
            {
                advertisement.ContentState = "UploadUnconfirmed";
                advertisement.ContentError = "Intelisa upload outcome is unknown. Reconcile the creative in Intelisa before retrying to avoid duplicate uploads.";
                await db.SaveChangesAsync(cancellationToken);
                logger.LogError("Intelisa upload outcome requires reconciliation for advertisement {AdvertisementId}.", message.AdvertisementId);
                return ContentProcessingResult.Complete;
            }

            var containerName = configuration["AzureBlob:ContainerName"];
            if (!string.Equals(containerName, "advertisements", StringComparison.Ordinal))
                throw new InvalidOperationException("AzureBlob:ContainerName must be advertisements, matching the API upload container.");
            var blob = blobServiceClient.GetBlobContainerClient(containerName).GetBlobClient(message.VideoBlobPath);
            await using var stream = await blob.OpenReadAsync(cancellationToken: cancellationToken);
            advertisement.ContentState = "Uploading";
            advertisement.ReadyForScheduling = false;
            advertisement.ContentError = null;
            await db.SaveChangesAsync(cancellationToken);

            try
            {
                var adGroup = $"{advertisement.Brand} - {advertisement.Id:D}";
                if (adGroup.Length > 100)
                    adGroup = adGroup[..100];
                var upload = await intelisa.UploadCreativeAsync(
                    stream,
                    Path.GetFileName(message.VideoBlobPath),
                    adGroup,
                    cancellationToken);
                advertisement.ContentId = upload.ContentId;
                advertisement.ContentAssetVersion = message.AssetVersion;
                advertisement.ContentSubmittedAtUtc = DateTime.UtcNow;
                advertisement.ContentState = "Processing";
                advertisement.ContentError = null;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (IntelisaRequestException exception) when (exception.OutcomeMayBeUnknown)
            {
                advertisement.ContentState = "UploadUnconfirmed";
                advertisement.ContentError = "Intelisa may have accepted the upload but did not confirm the response. Reconcile manually before retrying.";
                await db.SaveChangesAsync(cancellationToken);
                logger.LogError(exception, "Intelisa upload is unconfirmed for advertisement {AdvertisementId}.", message.AdvertisementId);
                return ContentProcessingResult.Complete;
            }
            catch (IntelisaRequestException exception) when (exception.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Unauthorized)
            {
                advertisement.ContentState = "Queued";
                advertisement.ContentError = null;
                await db.SaveChangesAsync(cancellationToken);
                throw;
            }
            catch (IntelisaRequestException exception) when (exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.Forbidden)
            {
                advertisement.ContentState = "Failed";
                advertisement.ContentError = "Intelisa rejected the creative or requires manual approval.";
                await db.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Intelisa rejected content for advertisement {AdvertisementId} with HTTP {StatusCode}.", message.AdvertisementId, (int)exception.StatusCode);
                return ContentProcessingResult.Complete;
            }
        }

        var contentId = advertisement.ContentId;
        if (string.IsNullOrWhiteSpace(contentId))
            return ContentProcessingResult.Complete;

        var status = await intelisa.GetCreativeStatusAsync(contentId, cancellationToken);
        if (string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase))
        {
            advertisement.ContentState = "Ready";
            advertisement.ReadyForScheduling = advertisement.Payment?.Status == PaymentStatus.Completed;
            advertisement.ReadyForSchedulingAtUtc = advertisement.ReadyForScheduling ? DateTime.UtcNow : null;
            advertisement.ContentError = null;
            await db.SaveChangesAsync(cancellationToken);
            return ContentProcessingResult.Complete;
        }

        if (string.Equals(status, "not_found", StringComparison.OrdinalIgnoreCase))
        {
            advertisement.ContentState = "Failed";
            advertisement.ReadyForScheduling = false;
            advertisement.ContentError = "Intelisa could not find the uploaded creative. Reconcile the creative ID before retrying.";
            await db.SaveChangesAsync(cancellationToken);
            return ContentProcessingResult.Complete;
        }

        if (message.PollAttempt >= 360)
        {
            advertisement.ContentState = "Failed";
            advertisement.ContentError = "Intelisa creative remained in processing for more than 12 hours.";
            await db.SaveChangesAsync(cancellationToken);
            return ContentProcessingResult.Complete;
        }

        advertisement.ContentState = "Processing";
        advertisement.ReadyForScheduling = false;
        await db.SaveChangesAsync(cancellationToken);
        return ContentProcessingResult.PollAgain;
    }

    private async Task AcquireContentLockAsync(Guid advertisementId, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @result;";
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@resource", $"ad-content-{advertisementId:D}"));
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0)
            throw new InvalidOperationException($"Content processing is already in progress for advertisement {advertisementId}.");
    }

    private async Task ReleaseContentLockAsync(Guid advertisementId)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner='Session';";
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@resource", $"ad-content-{advertisementId:D}"));
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> GetCurrentAssetVersionAsync(Guid advertisementId, CancellationToken cancellationToken)
    {
        return await db.ContentOutbox.Where(item => item.AdvertisementId == advertisementId)
            .MaxAsync(item => (int?)item.AssetVersion, cancellationToken) ?? 0;
    }
}
