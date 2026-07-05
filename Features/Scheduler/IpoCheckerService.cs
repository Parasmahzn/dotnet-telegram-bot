using Cronos;
using MeroShareBot.Shared.Config;
using Microsoft.Extensions.Options;

namespace MeroShareBot.Features.Scheduler;

// Port of startIpoScheduler from src/scheduler/ipoChecker.js — Cronos replaces node-cron.
public sealed class IpoCheckerService(
    IServiceScopeFactory scopeFactory,
    IOptions<SchedulerOptions> schedulerOpts,
    IOptions<MeroShareOptions> meroShareOpts,
    TimeProvider timeProvider,
    ILogger<IpoCheckerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (meroShareOpts.Value.Users.Count == 0)
        {
            logger.LogWarning("IPO scheduler: no users configured, skipping.");
            return;
        }

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
