namespace MeroShareBot.Shared.Telegram;

public sealed class BotUpdateHandler(IServiceScopeFactory scopeFactory, ILogger<BotUpdateHandler> logger)
{
    // Fire-and-forget so the webhook returns 200 immediately (Telegram's 5-second timeout).
    public void Dispatch(Update update)
    {
        _ = Task.Run(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<FeatureDispatcher>().DispatchAsync(update);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Telegram webhook error processing update {UpdateId}", update.Id);
                await NotifyChatAsync(scope.ServiceProvider, update);
            }
        });
    }

    private async Task NotifyChatAsync(IServiceProvider sp, Update update)
    {
        var chatId = update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id;
        if (chatId is null) return;

        try
        {
            await sp.GetRequiredService<TelegramSender>().SendTextAsync(chatId.Value,
                "⚠️ Something went wrong processing that. Please try again — if it keeps happening, contact the admin.");
        }
        catch (Exception sendEx)
        {
            logger.LogError(sendEx, "Failed to notify chat {ChatId} of a processing error", chatId);
        }
    }
}
