using Microsoft.EntityFrameworkCore;
using MeroShareBot.Shared.Data;
using MeroShareBot.Shared.Data.Entities;

namespace MeroShareBot.Features.Watchlist;

// Singleton — backed by MySQL via a short-lived BotDbContext per call (IDbContextFactory).
// Chat-scoped symbol lists, independent of any MeroShare account.
public sealed class WatchlistStore(IDbContextFactory<BotDbContext> factory)
{
    public IReadOnlyList<string> Get(long chatId)
    {
        using var db = factory.CreateDbContext();
        return [.. db.WatchlistItems.Where(w => w.ChatId == chatId).Select(w => w.Symbol).OrderBy(s => s)];
    }

    public bool Add(long chatId, string symbol)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        if (normalized.Length == 0) return false;

        using var db = factory.CreateDbContext();
        if (db.WatchlistItems.Find(chatId, normalized) is not null) return false;

        db.WatchlistItems.Add(new WatchlistEntity { ChatId = chatId, Symbol = normalized });
        db.SaveChanges();
        return true;
    }

    public bool Remove(long chatId, string symbol)
    {
        var normalized = symbol.Trim().ToUpperInvariant();

        using var db = factory.CreateDbContext();
        var entity = db.WatchlistItems.Find(chatId, normalized);
        if (entity is null) return false;

        db.WatchlistItems.Remove(entity);
        db.SaveChanges();
        return true;
    }
}
