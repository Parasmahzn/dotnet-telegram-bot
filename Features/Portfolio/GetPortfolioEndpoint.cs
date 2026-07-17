using System.Globalization;
using System.Text;
using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.Portfolio;

public sealed class GetPortfolioEndpoint(
    GetPortfolioHandler handler,
    AccountStore store,
    AccountResolver resolver,
    TelegramSender sender)
{
    private const int MaxMessageLength = 3500; // safety margin under Telegram's 4096 cap

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
            InlineKeyboardButton.WithCallbackData($"👤 {a.DisplayLabel} ({a.Username})", $"portfolio_acct_{i + 1}"),
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
            await sender.SendTextAsync(chatId, result.Error ?? $"Could not fetch portfolio for {account.DisplayLabel}.");
            return;
        }

        if (result.Portfolio.Items.Count == 0)
        {
            await sender.SendTextAsync(chatId, "📭 No portfolio holdings found.");
            return;
        }

        foreach (var message in BuildMessages(account.DisplayLabel, result.Portfolio))
            await sender.SendTextAsync(chatId, message);
    }

    private static IEnumerable<string> BuildMessages(string label, PortfolioResponse portfolio)
    {
        var totals =
            "📈 Totals\n\n" +
            $"Last Closing Value:\nRs. {portfolio.TotalValueOfPrevClosingPrice.ToString("N2", CultureInfo.InvariantCulture)}\n\n" +
            $"Last Transaction Value:\nRs. {portfolio.TotalValueOfLastTransPrice.ToString("N2", CultureInfo.InvariantCulture)}";

        var current = new StringBuilder($"📊 Portfolio — {label}\n\n");
        foreach (var item in portfolio.Items)
        {
            var block = FormatItem(item);
            if (current.Length + block.Length > MaxMessageLength)
            {
                yield return current.ToString().TrimEnd();
                current = new StringBuilder();
            }
            current.Append(block);
        }

        if (current.Length + totals.Length > MaxMessageLength)
        {
            yield return current.ToString().TrimEnd();
            yield return totals;
        }
        else
        {
            current.Append(totals);
            yield return current.ToString();
        }
    }

    private static string FormatItem(PortfolioItem item)
    {
        var name = string.IsNullOrEmpty(item.ScriptDesc) ? item.Script : $"{item.ScriptDesc} ({item.Script})";
        return
            $"🔹 {name}\n" +
            $"Balance: {item.CurrentBalance}\n" +
            $"Last Close: Rs. {FormatPrice(item.PreviousClosingPrice)}\n" +
            $"Value: Rs. {item.ValueOfPrevClosingPrice.ToString("N2", CultureInfo.InvariantCulture)}\n\n" +
            $"Last Trade: Rs. {FormatPrice(item.LastTransactionPrice)}\n" +
            $"Value: Rs. {item.ValueOfLastTransPrice.ToString("N2", CultureInfo.InvariantCulture)}\n\n" +
            "────────────────\n\n";
    }

    private static string FormatPrice(string? raw) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value.ToString("N2", CultureInfo.InvariantCulture)
            : raw ?? "-";
}
