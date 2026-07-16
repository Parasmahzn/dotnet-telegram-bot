namespace MeroShareBot.Shared.Data.Entities;

// One row per (chat, symbol). Composite PK — no surrogate id needed.
public sealed class WatchlistEntity
{
    public long ChatId { get; set; }
    public required string Symbol { get; set; }
}
