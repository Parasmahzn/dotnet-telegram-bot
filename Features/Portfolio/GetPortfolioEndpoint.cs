using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.Portfolio;

public sealed class GetPortfolioEndpoint(
    GetPortfolioHandler handler,
    AccountStore store,
    AccountResolver resolver,
    TelegramSender sender)
{
    public async Task HandleMessageAsync(Message msg, string arg)
    {
        var chatId = msg.Chat.Id;
        var account = resolver.Resolve(chatId, arg);
        if (account is not null)
        {
            await FetchAndSendAsync(chatId, account);
            return;
        }

        var accounts = store.GetAccounts(chatId);
        if (accounts.Count == 0)
        {
            await sender.SendTextAsync(chatId, "No accounts linked. Use /login to link one.");
            return;
        }

        var buttons = accounts.Select((a, i) => new[]
        {
            InlineKeyboardButton.WithCallbackData($"👤 {a.Username}", $"portfolio_acct_{i + 1}"),
        });
        await sender.SendKeyboardAsync(chatId, "Pick an account — which portfolio do you want to check?", buttons);
    }

    public async Task HandleCallbackAsync(CallbackQuery cb)
    {
        if (cb.Message is not { } cbMsg) return;
        var chatId = cbMsg.Chat.Id;
        await sender.AnswerCallbackAsync(cb.Id);

        var data = cb.Data ?? "";
        if (!data.StartsWith("portfolio_acct_") || !int.TryParse(data["portfolio_acct_".Length..], out var index)) return;

        var account = store.GetAccount(chatId, index);
        if (account is null) return;
        await FetchAndSendAsync(chatId, account);
    }

    private async Task FetchAndSendAsync(long chatId, LinkedAccount account)
    {
        var creds = store.Decrypt(account);
        var result = await handler.HandleAsync(creds.Credentials, text => sender.SendTextAsync(chatId, text));

        if (!result.Success || result.Portfolio is null)
        {
            await sender.SendTextAsync(chatId, result.Error ?? $"Could not fetch portfolio for {account.Username}.");
            return;
        }

        var items = result.Portfolio.Items;
        if (items.Count == 0)
        {
            await sender.SendTextAsync(chatId, $"📊 {account.Username} has no holdings.");
            return;
        }

        var lines = new List<string> { $"📊 Portfolio — {account.Username} ({items.Count} holding{(items.Count > 1 ? "s" : "")})", "" };
        foreach (var item in items)
        {
            lines.Add($"• {item.Script}{(string.IsNullOrEmpty(item.ScriptDesc) ? "" : $" — {item.ScriptDesc}")}");
            lines.Add($"   Qty: {item.CurrentBalance ?? "-"}  ·  LTP: {item.LastTransactionPrice ?? "-"}  ·  Value: {item.ValueAsOfLastTransactionPrice ?? "-"}");
        }
        if (result.Portfolio.TotalValueAsOfLastTransactionPrice is { } total)
        {
            lines.Add("");
            lines.Add($"💰 Total value (LTP): {total}");
        }

        await sender.SendTextAsync(chatId, string.Join('\n', lines));
    }
}
