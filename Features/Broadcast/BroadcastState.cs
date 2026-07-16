using System.Collections.Concurrent;

namespace MeroShareBot.Features.Broadcast;

public enum BroadcastStep { Selecting, AwaitingMessage, Confirming }

public sealed record PendingBroadcast(
    BroadcastStep Step,
    IReadOnlyList<UserRecord> Candidates,
    IReadOnlySet<long> Selected,
    string? MessageText = null);

// Singleton — owns the multi-step checkbox-picker state across webhook requests.
public sealed class BroadcastState
{
    private readonly ConcurrentDictionary<long, PendingBroadcast> _store = new();

    public void Set(long chatId, PendingBroadcast pending) => _store[chatId] = pending;

    public PendingBroadcast? Get(long chatId) => _store.TryGetValue(chatId, out var pending) ? pending : null;

    public void Remove(long chatId) => _store.TryRemove(chatId, out _);
}
