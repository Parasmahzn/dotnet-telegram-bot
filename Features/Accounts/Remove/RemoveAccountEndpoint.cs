using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.Accounts.Remove;

public sealed class RemoveAccountEndpoint(AccountStore store, TelegramSender sender)
{
    public async Task HandleAsync(Message msg, string arg)
    {
        var chatId = msg.Chat.Id;
        var accounts = store.GetAccounts(chatId);
        if (accounts.Count == 0)
        {
            await sender.SendTextAsync(chatId, "No accounts linked. Use /login to link one.");
            return;
        }

        var buttons = accounts
            .Select((a, i) => new[]
            {
                InlineKeyboardButton.WithCallbackData($"🗑️ {a.DisplayLabel} ({a.Username})", $"removeaccount_{i + 1}"),
            })
            .Append([InlineKeyboardButton.WithCallbackData("❌ Cancel", "removeaccount_cancel")]);
        await sender.SendKeyboardAsync(chatId, "Which account do you want to remove?", buttons);
    }

    public async Task HandleCallbackAsync(CallbackQuery cb)
    {
        if (cb.Message is not { } cbMsg) return;
        var chatId = cbMsg.Chat.Id;
        await sender.AnswerCallbackAsync(cb.Id);

        var data = cb.Data ?? "";
        if (data == "removeaccount_cancel")
        {
            await sender.SendTextAsync(chatId, "❌ Cancelled.");
            return;
        }

        if (!data.StartsWith("removeaccount_") || !int.TryParse(data["removeaccount_".Length..], out var index)) return;

        var account = store.GetAccount(chatId, index);
        if (account is null)
        {
            await sender.SendTextAsync(chatId, "❌ Invalid account index. Run /accounts to see valid indices.");
            return;
        }

        store.RemoveAccount(chatId, index);
        await sender.SendTextAsync(chatId, $"🗑️ Removed account #{index} ({account.DisplayLabel}).");
    }
}
