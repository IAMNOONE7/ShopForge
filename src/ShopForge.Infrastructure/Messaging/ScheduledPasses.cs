using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShopForge.Shared.Maintenance;

namespace ShopForge.Infrastructure.Messaging;

// Keeps every scheduled pass: asks the schedule a few times a period whether the pass is due, and runs it when this
// replica is the one that took it. The first ask is at start, so work that fell due while the process was down is
// done at once instead of a full period later (D-202).
internal sealed class ScheduledPasses(
    IEnumerable<IScheduledPass> passes,
    RunSchedule schedule,
    ScheduleOptions options,
    TimeProvider clock,
    ILogger<ScheduledPasses> logger) : BackgroundService
{
    private static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(1);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        options.Enabled ? Task.WhenAll(passes.Select(pass => KeepAsync(pass, stoppingToken))) : Task.CompletedTask;

    private async Task KeepAsync(IScheduledPass pass, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(WaitFor(pass.Period), clock);

        do
        {
            try
            {
                if (await schedule.ClaimAsync(pass.Name, pass.Period, stoppingToken))
                {
                    await pass.RunAsync(stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "The {Pass} pass failed; it is due again in {Period}.", pass.Name, pass.Period);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    // Often enough that a pass starts within a quarter of its period of falling due, and at most once a minute,
    // which costs an update that matches nothing.
    private static TimeSpan WaitFor(TimeSpan period) => period / 4 < LongestWait ? period / 4 : LongestWait;
}

// Off only where something else drives the passes: the integration tests, whose hosts share one database without
// sharing their stand-ins for the outside world, and call each pass themselves.
internal sealed record ScheduleOptions(bool Enabled);
