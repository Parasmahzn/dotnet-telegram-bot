using System.Globalization;
using System.Text;
using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.Portfolio;

public sealed class GetPortfolioEndpoint(
    GetPortfolioHandler handler,
    AccountStore store,
    AccountResolver resolver,
    PortfolioViewState viewState,
    TelegramSender sender)
{
    private const int PageSize = 5;

    public async Task HandleMessageAsync(Message msg, string arg)
    {
        var chatId = msg.Chat.Id;
        var account = resolver.Resolve(chatId, arg);
        if (account is not null)
        {
            var accountList = store.GetAccounts(chatId);
            var index = accountList.ToList().FindIndex(a => a.Id == account.Id) + 1;
            await RenderSummaryAsync(chatId, messageId: null, account, index);
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
        var messageId = cbMsg.MessageId;
        var data = cb.Data ?? "";

        if (data.StartsWith("portfolio_acct_") && int.TryParse(data["portfolio_acct_".Length..], out var acctIndex))
        {
            await sender.AnswerCallbackAsync(cb.Id);
            var account = store.GetAccount(chatId, acctIndex);
            if (account is null) return;
            await RenderSummaryAsync(chatId, messageId, account, acctIndex);
            return;
        }

        if (data.StartsWith("portfolio_summary_") && int.TryParse(data["portfolio_summary_".Length..], out var summaryIndex))
        {
            await sender.AnswerCallbackAsync(cb.Id);
            var account = store.GetAccount(chatId, summaryIndex);
            if (account is null) return;
            await RenderSummaryAsync(chatId, messageId, account, summaryIndex);
            return;
        }

        if (data.StartsWith("portfolio_hold_"))
        {
            var rest = data["portfolio_hold_".Length..];
            var parts = rest.Split('_');
            if (parts.Length == 2 && int.TryParse(parts[0], out var holdIndex) && int.TryParse(parts[1], out var page))
            {
                await sender.AnswerCallbackAsync(cb.Id);
                var account = store.GetAccount(chatId, holdIndex);
                if (account is null) return;
                var view = viewState.Get(chatId, holdIndex) with { Page = page };
                viewState.Set(chatId, view);
                await RenderHoldingsAsync(chatId, messageId, account, holdIndex, view);
            }
            return;
        }

        if (data.StartsWith("portfolio_sort_") && int.TryParse(data["portfolio_sort_".Length..], out var sortIndex))
        {
            await sender.AnswerCallbackAsync(cb.Id);
            var account = store.GetAccount(chatId, sortIndex);
            if (account is null) return;
            var view = viewState.Get(chatId, sortIndex) with { SortDesc = !viewState.Get(chatId, sortIndex).SortDesc, Page = 0 };
            viewState.Set(chatId, view);
            await RenderHoldingsAsync(chatId, messageId, account, sortIndex, view);
            return;
        }

        if (data.StartsWith("portfolio_search_") && int.TryParse(data["portfolio_search_".Length..], out var searchIndex))
        {
            await sender.AnswerCallbackAsync(cb.Id);
            viewState.StartSearch(chatId, searchIndex);
            await sender.SendTextAsync(chatId, "🔍 Send a script symbol/name to filter (or \"cancel\").");
            return;
        }

        if (data.StartsWith("portfolio_clearsearch_") && int.TryParse(data["portfolio_clearsearch_".Length..], out var clearIndex))
        {
            await sender.AnswerCallbackAsync(cb.Id);
            var account = store.GetAccount(chatId, clearIndex);
            if (account is null) return;
            var view = viewState.Get(chatId, clearIndex) with { SearchTerm = null, Page = 0 };
            viewState.Set(chatId, view);
            await RenderHoldingsAsync(chatId, messageId, account, clearIndex, view);
            return;
        }

        if (data.StartsWith("portfolio_csv_") && int.TryParse(data["portfolio_csv_".Length..], out var csvIndex))
        {
            await sender.AnswerCallbackAsync(cb.Id);
            var account = store.GetAccount(chatId, csvIndex);
            if (account is null) return;
            await SendCsvAsync(chatId, account, csvIndex);
            return;
        }

        await sender.AnswerCallbackAsync(cb.Id);
    }

    public async Task HandleSearchReplyAsync(Message msg)
    {
        var chatId = msg.Chat.Id;
        var text = (msg.Text ?? "").Trim();

        // AwaitingSearch was set against whichever account index was active — find it back out.
        var accounts = store.GetAccounts(chatId);
        var index = -1;
        for (var i = 0; i < accounts.Count; i++)
        {
            if (viewState.Get(chatId, i + 1).AwaitingSearch) { index = i + 1; break; }
        }
        if (index == -1) return;

        var account = store.GetAccount(chatId, index);
        if (account is null) return;

        var view = viewState.Get(chatId, index) with
        {
            AwaitingSearch = false,
            SearchTerm = text.Equals("cancel", StringComparison.OrdinalIgnoreCase) ? null : text,
            Page = 0,
        };
        viewState.Set(chatId, view);
        await RenderHoldingsAsync(chatId, messageId: null, account, index, view);
    }

    private async Task RenderSummaryAsync(long chatId, int? messageId, LinkedAccount account, int index)
    {
        var result = await FetchAsync(chatId, account);
        if (!result.Success || result.Portfolio is null)
        {
            var error = result.Error ?? $"Could not fetch portfolio for {account.DisplayLabel}.";
            if (messageId is null) await sender.SendTextAsync(chatId, error);
            else await sender.EditTextAsync(chatId, messageId.Value, error);
            return;
        }

        var portfolio = result.Portfolio;
        if (portfolio.Items.Count == 0)
        {
            const string empty = "📭 No portfolio holdings found.";
            if (messageId is null) await sender.SendTextAsync(chatId, empty);
            else await sender.EditTextAsync(chatId, messageId.Value, empty);
            return;
        }

        var change = portfolio.TotalValueOfLastTransPrice - portfolio.TotalValueOfPrevClosingPrice;
        var pct = portfolio.TotalValueOfPrevClosingPrice == 0
            ? 0
            : change / portfolio.TotalValueOfPrevClosingPrice * 100;
        var sign = change >= 0 ? "+" : "-";

        var topFive = portfolio.Items
            .OrderByDescending(i => i.ValueOfLastTransPrice)
            .Take(5)
            .Select(i => $"{PadName(i.Script)} Rs. {i.ValueOfLastTransPrice.ToString("N0", CultureInfo.InvariantCulture)}");

        var text =
            $"📊 Portfolio — {account.DisplayLabel}\n\n" +
            $"💼 Holdings: {portfolio.Items.Count}\n\n" +
            "💰 Total Value\n" +
            "──────────────\n" +
            $"Last Close : Rs. {portfolio.TotalValueOfPrevClosingPrice.ToString("N2", CultureInfo.InvariantCulture)}\n" +
            $"Last Trade : Rs. {portfolio.TotalValueOfLastTransPrice.ToString("N2", CultureInfo.InvariantCulture)}\n\n" +
            "📈 Change\n" +
            $"{sign}Rs. {Math.Abs(change).ToString("N2", CultureInfo.InvariantCulture)} ({sign}{Math.Abs(pct).ToString("N2", CultureInfo.InvariantCulture)}%)\n\n" +
            "🏆 Top 5 Holdings\n" +
            string.Join('\n', topFive);

        IEnumerable<InlineKeyboardButton[]> buttons =
        [
            [
                InlineKeyboardButton.WithCallbackData("📋 View Holdings", $"portfolio_hold_{index}_0"),
                InlineKeyboardButton.WithCallbackData("🔍 Search Script", $"portfolio_search_{index}"),
            ],
            [
                InlineKeyboardButton.WithCallbackData("📊 Sort by Value", $"portfolio_sort_{index}"),
                InlineKeyboardButton.WithCallbackData("📄 Export CSV", $"portfolio_csv_{index}"),
            ],
        ];

        if (messageId is null) await sender.SendKeyboardAsync(chatId, text, buttons);
        else await sender.EditKeyboardAsync(chatId, messageId.Value, text, buttons);
    }

    private async Task RenderHoldingsAsync(long chatId, int? messageId, LinkedAccount account, int index, PortfolioView view)
    {
        var result = await FetchAsync(chatId, account);
        if (!result.Success || result.Portfolio is null)
        {
            var error = result.Error ?? $"Could not fetch portfolio for {account.DisplayLabel}.";
            if (messageId is null) await sender.SendTextAsync(chatId, error);
            else await sender.EditTextAsync(chatId, messageId.Value, error);
            return;
        }

        var filtered = Filter(result.Portfolio.Items, view);
        if (filtered.Count == 0)
        {
            var emptyText = string.IsNullOrEmpty(view.SearchTerm)
                ? "📭 No portfolio holdings found."
                : $"📭 No holdings match \"{view.SearchTerm}\".";
            IEnumerable<InlineKeyboardButton[]> emptyButtons =
            [
                [InlineKeyboardButton.WithCallbackData("◀️ Back", $"portfolio_summary_{index}")],
            ];
            if (messageId is null) await sender.SendKeyboardAsync(chatId, emptyText, emptyButtons);
            else await sender.EditKeyboardAsync(chatId, messageId.Value, emptyText, emptyButtons);
            return;
        }

        var totalPages = (int)Math.Ceiling(filtered.Count / (double)PageSize);
        var page = Math.Clamp(view.Page, 0, totalPages - 1);
        var pageItems = filtered.Skip(page * PageSize).Take(PageSize);

        var sb = new StringBuilder($"📋 Holdings ({page + 1}/{totalPages})");
        if (!string.IsNullOrEmpty(view.SearchTerm)) sb.Append($" — filter: \"{view.SearchTerm}\"");
        sb.Append("\n\n");
        foreach (var item in pageItems)
        {
            sb.Append($"{item.Script}\n");
            sb.Append($"Qty:{item.CurrentBalance.ToString("0.##", CultureInfo.InvariantCulture)} | LTP:{FormatPrice(item.LastTransactionPrice)}\n");
            sb.Append($"Value:{item.ValueOfLastTransPrice.ToString("N0", CultureInfo.InvariantCulture)}\n\n");
        }

        var navRow = new List<InlineKeyboardButton>();
        if (page > 0) navRow.Add(InlineKeyboardButton.WithCallbackData("⬅️", $"portfolio_hold_{index}_{page - 1}"));
        navRow.Add(InlineKeyboardButton.WithCallbackData($"{page + 1}/{totalPages}", $"portfolio_hold_{index}_{page}"));
        if (page < totalPages - 1) navRow.Add(InlineKeyboardButton.WithCallbackData("➡️", $"portfolio_hold_{index}_{page + 1}"));

        var actionRow = new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData(view.SortDesc ? "📊 Sort: Low→High" : "📊 Sort: High→Low", $"portfolio_sort_{index}"),
        };
        actionRow.Add(string.IsNullOrEmpty(view.SearchTerm)
            ? InlineKeyboardButton.WithCallbackData("🔍 Search Script", $"portfolio_search_{index}")
            : InlineKeyboardButton.WithCallbackData("✖️ Clear search", $"portfolio_clearsearch_{index}"));

        IEnumerable<InlineKeyboardButton[]> buttons =
        [
            [.. navRow],
            [.. actionRow],
            [
                InlineKeyboardButton.WithCallbackData("📄 Export CSV", $"portfolio_csv_{index}"),
                InlineKeyboardButton.WithCallbackData("◀️ Back", $"portfolio_summary_{index}"),
            ],
        ];

        var text = sb.ToString().TrimEnd();
        if (messageId is null) await sender.SendKeyboardAsync(chatId, text, buttons);
        else await sender.EditKeyboardAsync(chatId, messageId.Value, text, buttons);
    }

    private async Task SendCsvAsync(long chatId, LinkedAccount account, int index)
    {
        var result = await FetchAsync(chatId, account);
        if (!result.Success || result.Portfolio is null)
        {
            await sender.SendTextAsync(chatId, result.Error ?? $"Could not fetch portfolio for {account.DisplayLabel}.");
            return;
        }

        var view = viewState.Get(chatId, index);
        var filtered = Filter(result.Portfolio.Items, view);
        if (filtered.Count == 0)
        {
            await sender.SendTextAsync(chatId, "📭 No holdings to export.");
            return;
        }

        var csv = BuildCsv(filtered);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        await sender.SendDocumentAsync(chatId, stream, $"portfolio-{account.DisplayLabel}.csv", $"📄 Portfolio export — {account.DisplayLabel}");
    }

    private static List<PortfolioItem> Filter(List<PortfolioItem> items, PortfolioView view)
    {
        IEnumerable<PortfolioItem> query = items;
        if (!string.IsNullOrEmpty(view.SearchTerm))
        {
            query = query.Where(i =>
                i.Script.Contains(view.SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                (i.ScriptDesc?.Contains(view.SearchTerm, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        query = view.SortDesc
            ? query.OrderByDescending(i => i.ValueOfLastTransPrice)
            : query.OrderBy(i => i.ValueOfLastTransPrice);
        return [.. query];
    }

    private static string BuildCsv(IEnumerable<PortfolioItem> items)
    {
        var sb = new StringBuilder("Script,ScriptDesc,Qty,LastTransactionPrice,PreviousClosingPrice,ValueOfLastTransPrice,ValueOfPrevClosingPrice\n");
        foreach (var i in items)
        {
            sb.Append($"\"{i.Script}\",\"{i.ScriptDesc}\",{i.CurrentBalance.ToString(CultureInfo.InvariantCulture)},");
            sb.Append($"{i.LastTransactionPrice},{i.PreviousClosingPrice},");
            sb.Append($"{i.ValueOfLastTransPrice.ToString(CultureInfo.InvariantCulture)},{i.ValueOfPrevClosingPrice.ToString(CultureInfo.InvariantCulture)}\n");
        }
        return sb.ToString();
    }

    private Task<PortfolioResult> FetchAsync(long chatId, LinkedAccount account)
    {
        var creds = store.Decrypt(account);
        return handler.HandleAsync(creds.Credentials, text => sender.SendTextAsync(chatId, text));
    }

    private static string PadName(string script) => script.Length >= 8 ? script[..8] : script.PadRight(8);

    private static string FormatPrice(string? raw) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value.ToString("N2", CultureInfo.InvariantCulture)
            : raw ?? "-";
}
