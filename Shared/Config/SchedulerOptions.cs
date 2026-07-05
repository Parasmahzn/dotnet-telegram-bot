namespace MeroShareBot.Shared.Config;

public sealed class SchedulerOptions
{
    // Default: 10:05 AM NPT (04:20 UTC). Override with Scheduler:IpoCron for testing.
    public string IpoCron { get; init; } = "20 4 * * *";
}
