using Adrover.Functions.Services;
using Microsoft.Azure.Functions.Worker;

namespace Adrover.Functions.Functions;

public sealed class ContentOutboxDispatchFunction(ContentOutboxPublisher publisher, ILogger<ContentOutboxDispatchFunction> logger)
{
    [Function(nameof(ContentOutboxDispatchFunction))]
    public async Task Run([TimerTrigger("*/30 * * * * *")] TimerInfo timer, CancellationToken cancellationToken)
    {
        logger.LogDebug("Dispatching pending advertisement content outbox rows.");
        await publisher.PublishPendingAsync(cancellationToken);
    }
}
