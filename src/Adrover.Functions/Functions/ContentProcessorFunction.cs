using System.Text.Json;
using Adrover.Functions.Configuration;
using Adrover.Functions.Contracts;
using Adrover.Functions.Services;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;

namespace Adrover.Functions.Functions;

public sealed class ContentProcessorFunction(
    ContentProcessor processor,
    ServiceBusClient serviceBusClient,
    ILogger<ContentProcessorFunction> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Function(nameof(ContentProcessorFunction))]
    public async Task Run(
        [ServiceBusTrigger("ad-content-processing", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        ContentProcessingMessage? body;
        try
        {
            body = JsonSerializer.Deserialize<ContentProcessingMessage>(message.Body, JsonOptions);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Invalid content processing message {MessageId}.", message.MessageId);
            throw new InvalidDataException("Invalid content processing message.", exception);
        }

        if (body is null || body.MessageType != "AdContentReady.v1" || body.AdvertisementId == Guid.Empty || body.AssetVersion < 1)
        {
            logger.LogError("Invalid content processing message {MessageId}.", message.MessageId);
            throw new InvalidDataException("Invalid content processing message contract.");
        }

        var result = await processor.ProcessAsync(body, cancellationToken);
        if (result == ContentProcessingResult.PollAgain)
        {
            var nextAttempt = body with { PollAttempt = body.PollAttempt + 1 };
            var pollMessage = new ServiceBusMessage(BinaryData.FromObjectAsJson(nextAttempt, JsonOptions))
            {
                MessageId = $"content-{body.AdvertisementId:D}-v{body.AssetVersion}-poll-{nextAttempt.PollAttempt}",
                ContentType = "application/json"
            };
            await using var sender = serviceBusClient.CreateSender(ServiceBusOptions.QueueName);
            await sender.ScheduleMessageAsync(pollMessage, DateTimeOffset.UtcNow.AddMinutes(2), cancellationToken);
        }
    }
}
