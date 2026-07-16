namespace MeroShareBot.Features.Watchlist;

public sealed class WatchlistEndpoint(WatchlistStore store, TelegramSender sender)
{
    public async Task HandleAsync(Message msg, string arg)
    {
        var chatId = msg.Chat.Id;
        var parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            await ListAsync(chatId);
            return;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "add" when parts.Length == 2:
                var added = store.Add(chatId, parts[1]);
                await sender.SendTextAsync(chatId, added
                    ? $"✅ Added {parts[1].Trim().ToUpperInvariant()} to your watchlist."
                    : $"{parts[1].Trim().ToUpperInvariant()} is already on your watchlist.");
                break;
            case "remove" when parts.Length == 2:
                var removed = store.Remove(chatId, parts[1]);
                await sender.SendTextAsync(chatId, removed
                    ? $"🗑️ Removed {parts[1].Trim().ToUpperInvariant()}."
                    : "Symbol not on your watchlist.");
                break;
            default:
                await sender.SendTextAsync(chatId, "Usage: /watch, /watch add SYMBOL, /watch remove SYMBOL");
                break;
        }
    }

    private async Task ListAsync(long chatId)
    {
        var symbols = store.Get(chatId);
        await sender.SendTextAsync(chatId, symbols.Count == 0
            ? "Your watchlist is empty. Use /watch add SYMBOL."
            : "👀 Watchlist:\n" + string.Join('\n', symbols.Select(s => $"• {s}")));
    }
}
