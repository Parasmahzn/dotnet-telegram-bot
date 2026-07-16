using Cronos;

namespace MeroShareBot.Features.Scheduler;

// Parses Scheduler:IpoCron with Cronos and sleeps until the next occurrence.
public sealed class IpoCheckerService(
    IServiceScopeFactory scopeFactory,
    IOptions<SchedulerOptions> schedulerOpts,
    TimeProvider timeProvider,
    ILogger<IpoCheckerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var cronText = schedulerOpts.Value.IpoCron;
        var cron = CronExpression.Parse(cronText);
        logger.LogInformation("IPO scheduler started (cron: \"{Cron}\").", cronText);

        while (!ct.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var next = cron.GetNextOccurrence(now, inclusive: false);
            if (next is null) break;

            logger.LogInformation("Next IPO check at {Next:u} (in {Delay}).", next.Value, next.Value - now);
            await Task.Delay(next.Value - now, timeProvider, ct);

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IpoCheckerJob>().RunAsync(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[IPO Scheduler] Error");
            }
        }
    }
}
