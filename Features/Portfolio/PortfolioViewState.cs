using System.Collections.Concurrent;

namespace MeroShareBot.Features.Portfolio;

public sealed record PortfolioView(int AccountIndex, int Page, bool SortDesc, string? SearchTerm, bool AwaitingSearch);

// Singleton — tracks per-chat holdings-view state (which account, page, sort order, search filter)
// across callback taps. Callback_data alone can't carry a search string reliably (64-byte cap), and
// portfolio data itself is never cached here — every render re-fetches live via GetPortfolioHandler.
public sealed class PortfolioViewState
{
    private readonly ConcurrentDictionary<long, PortfolioView> _state = new();

    public PortfolioView Get(long chatId, int accountIndex) =>
        _state.TryGetValue(chatId, out var v) && v.AccountIndex == accountIndex
            ? v
            : new PortfolioView(accountIndex, Page: 0, SortDesc: true, SearchTerm: null, AwaitingSearch: false);

    public void Set(long chatId, PortfolioView view) => _state[chatId] = view;

    public void StartSearch(long chatId, int accountIndex) =>
        _state[chatId] = Get(chatId, accountIndex) with { AwaitingSearch = true };

    public bool IsAwaitingSearch(long chatId) =>
        _state.TryGetValue(chatId, out var v) && v.AwaitingSearch;

    public void Clear(long chatId) => _state.TryRemove(chatId, out _);
}
