using Telegram.Bot.Types;

namespace MeroShareBot.Shared.Telegram;

public sealed class BotUpdateHandler(IServiceScopeFactory scopeFactory, ILogger<BotUpdateHandler> logger)
{
    // Fire-and-forget so the webhook returns 200 immediately (Telegram's 5-second timeout).
    public void Dispatch(Update update)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<FeatureDispatcher>().DispatchAsync(update);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Telegram webhook error processing update {UpdateId}", update.Id);
            }
        });
    }
}
