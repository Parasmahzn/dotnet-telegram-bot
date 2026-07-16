using System.Collections.Concurrent;
using System.Text.Json;

namespace MeroShareBot.Features.Watchlist;

// Singleton, JSON-file-backed (data/watchlists.json) — chat-scoped symbol lists, independent of
// any MeroShare account.
public sealed class WatchlistStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger<WatchlistStore> _logger;
    private readonly ConcurrentDictionary<long, HashSet<string>> _byChat = new();
    private readonly object _saveLock = new();

    public WatchlistStore(IHostEnvironment env, ILogger<WatchlistStore> logger)
    {
        _logger = logger;
        _path = Path.Combine(env.ContentRootPath, "data", "watchlists.json");
        Load();
    }

    private void Load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (!File.Exists(_path)) return;

        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<long, HashSet<string>>>(File.ReadAllText(_path));
            if (loaded is null) return;
            foreach (var (chatId, symbols) in loaded) _byChat[chatId] = symbols;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load {Path}", _path);
        }
    }

    private void Save()
    {
        lock (_saveLock)
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_byChat.ToDictionary(kv => kv.Key, kv => kv.Value), Json));
        }
    }

    public IReadOnlyList<string> Get(long chatId) =>
        _byChat.TryGetValue(chatId, out var symbols) ? [.. symbols.Order()] : [];

    public bool Add(long chatId, string symbol)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        if (normalized.Length == 0) return false;
        var set = _byChat.GetOrAdd(chatId, _ => []);
        var added = set.Add(normalized);
        if (added) Save();
        return added;
    }

    public bool Remove(long chatId, string symbol)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        if (!_byChat.TryGetValue(chatId, out var set)) return false;
        var removed = set.Remove(normalized);
        if (removed) Save();
        return removed;
    }
}
