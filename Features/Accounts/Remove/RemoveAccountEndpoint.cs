namespace MeroShareBot.Features.Accounts.Remove;

public sealed class RemoveAccountEndpoint(AccountStore store, TelegramSender sender)
{
    public async Task HandleAsync(Message msg, string arg)
    {
        var chatId = msg.Chat.Id;
        if (!int.TryParse(arg, out var index))
        {
            await sender.SendTextAsync(chatId, "Usage: /removeaccount <n> — run /accounts to see the index of each linked account.");
            return;
        }

        var account = store.GetAccount(chatId, index);
        if (account is null)
        {
            await sender.SendTextAsync(chatId, "❌ Invalid account index. Run /accounts to see valid indices.");
            return;
        }

        store.RemoveAccount(chatId, index);
        await sender.SendTextAsync(chatId, $"🗑️ Removed account #{index} ({account.Username} · {account.Dp}).");
    }
}
