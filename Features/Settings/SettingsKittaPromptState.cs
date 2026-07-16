using System.Collections.Concurrent;

namespace MeroShareBot.Features.Settings;

// Singleton — tracks which account index a chat is being asked for a kitta amount for, while
// /settings waits on a free-text reply.
public sealed class SettingsKittaPromptState
{
    private readonly ConcurrentDictionary<long, int> _pending = new();

    public void Await(long chatId, int accountIndex) => _pending[chatId] = accountIndex;

    public int? Get(long chatId) => _pending.TryGetValue(chatId, out var index) ? index : null;

    public void Clear(long chatId) => _pending.TryRemove(chatId, out _);
}
