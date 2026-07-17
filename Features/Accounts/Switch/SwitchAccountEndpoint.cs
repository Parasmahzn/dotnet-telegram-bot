namespace MeroShareBot.Features.Accounts.Switch;

public sealed class SwitchAccountEndpoint(AccountStore store, TelegramSender sender)
{
    public async Task HandleAsync(Message msg, string arg)
    {
        var chatId = msg.Chat.Id;
        if (!int.TryParse(arg, out var index))
        {
            await sender.SendTextAsync(chatId, "Usage: /switch <n> — run /accounts to see the index of each linked account.");
            return;
        }

        var account = store.GetAccount(chatId, index);
        if (account is null || !store.SetDefault(chatId, index))
        {
            await sender.SendTextAsync(chatId, "❌ Invalid account index. Run /accounts to see valid indices.");
            return;
        }

        await sender.SendTextAsync(chatId, $"✅ Default account set to #{index} ({account.DisplayLabel}).");
    }
}
