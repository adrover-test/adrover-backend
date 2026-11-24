using Adrover.Functions.Services;
using Microsoft.Azure.Functions.Worker;

namespace Adrover.Functions.Functions;

public sealed class DailySchedulerFunction(DailyScheduler scheduler, ILogger<DailySchedulerFunction> logger)
{
    [Function(nameof(DailySchedulerFunction))]
    public async Task Run([TimerTrigger("0 30 0 * * *", UseMonitor = true)] TimerInfo timer, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting the IST daily advertisement scheduling and proof-of-play run.");
        await scheduler.RunAsync(cancellationToken);
    }
}
