namespace MeroShareBot.Features.Notify;

public sealed class NotifyEndpoint(AccountStore store, TelegramSender sender)
{
    public async Task HandleAsync(Message msg, string arg)
    {
        var chatId = msg.Chat.Id;
        var normalized = arg.Trim().ToLowerInvariant();

        var enabled = normalized switch
        {
            "on" => true,
            "off" => false,
            _ => !store.GetChatNotify(chatId), // bare /notify toggles
        };

        store.SetChatNotify(chatId, enabled);
        await sender.SendTextAsync(chatId, enabled
            ? "🔔 Daily IPO notifications enabled for this chat."
            : "🔕 Daily IPO notifications disabled for this chat.");
    }
}
