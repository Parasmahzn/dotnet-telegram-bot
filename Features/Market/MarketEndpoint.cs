namespace MeroShareBot.Features.Market;

public sealed class MarketEndpoint(TelegramSender sender)
{
    public Task HandleAsync(Message msg, string arg) =>
        sender.SendTextAsync(msg.Chat.Id, "📈 Market data isn't available yet — coming in a future update.");
}
