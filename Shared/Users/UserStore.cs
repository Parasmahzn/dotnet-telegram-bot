using System.Collections.Concurrent;
using System.Text.Json;

namespace MeroShareBot.Shared.Users;

// Singleton, JSON-file-backed (data/users.json) — same Load/Save/lock pattern as AccountStore.
// Registration registry only: no MeroShare credentials live here, those stay in AccountStore.
public sealed class UserStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger<UserStore> _logger;
    private readonly ConcurrentDictionary<long, UserRecord> _users = new();
    private readonly object _saveLock = new();

    public UserStore(IHostEnvironment env, ILogger<UserStore> logger)
    {
        _logger = logger;
        _path = Path.Combine(env.ContentRootPath, "data", "users.json");
        Load();
    }

    private void Load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (!File.Exists(_path)) return;

        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<long, UserRecord>>(File.ReadAllText(_path));
            if (loaded is null) return;
            foreach (var (chatId, user) in loaded) _users[chatId] = user;
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
            File.WriteAllText(_path, JsonSerializer.Serialize(_users.ToDictionary(kv => kv.Key, kv => kv.Value), Json));
        }
    }

    public void RegisterUser(long chatId, string firstName, string lastName, string username)
    {
        var existing = _users.GetValueOrDefault(chatId);
        var updated = new UserRecord(chatId, firstName, lastName, username,
            existing?.RegisteredAt ?? DateTimeOffset.UtcNow, existing?.IsAdmin ?? false);

        if (existing == updated) return; // records compare by value — no-op if nothing changed
        _users[chatId] = updated;
        Save();
    }

    public IReadOnlyList<long> GetAllChatIds() => [.. _users.Keys];

    public UserRecord? GetUser(long chatId) => _users.GetValueOrDefault(chatId);

    public bool IsAdmin(long chatId) => GetUser(chatId)?.IsAdmin == true;
}
