using System.Text.Json;
using Adrover.Data.Context;
using Adrover.Functions.Configuration;
using Adrover.Functions.Contracts;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;

namespace Adrover.Functions.Services;

public sealed class ContentOutboxPublisher(
    AdRoverDbContext db,
    ServiceBusClient serviceBusClient,
    ILogger<ContentOutboxPublisher> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishPendingAsync(CancellationToken cancellationToken)
    {
        var rows = await db.ContentOutbox
            .Where(item => item.PublishedAtUtc == null && item.NextAttemptAtUtc <= DateTime.UtcNow)
            .OrderBy(item => item.CreatedAtUtc)
            .Take(25)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return;

        await using var sender = serviceBusClient.CreateSender(ServiceBusOptions.QueueName);
        foreach (var row in rows)
        {
            var body = new ContentProcessingMessage(
                "AdContentReady.v1",
                row.AdvertisementId,
                row.AssetVersion,
                row.VideoBlobPath,
                row.PaymentId,
                row.CreatedAtUtc);
            var message = new ServiceBusMessage(BinaryData.FromObjectAsJson(body, JsonOptions))
            {
                MessageId = row.MessageId,
                ContentType = "application/json"
            };
            try
            {
                await sender.SendMessageAsync(message, cancellationToken);
                row.PublishedAtUtc = DateTime.UtcNow;
                row.LastError = null;
                logger.LogInformation("Published content outbox message {MessageId}.", row.MessageId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                row.Attempts++;
                row.LastError = "Service Bus publish failed.";
                row.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(Math.Min(3600, 15 * Math.Pow(2, Math.Min(row.Attempts, 8))));
                logger.LogError(exception, "Unable to publish content outbox message {MessageId}.", row.MessageId);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
