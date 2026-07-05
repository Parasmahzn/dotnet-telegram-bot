using System.Collections.Concurrent;
using MeroShareBot.Shared.Config;

namespace MeroShareBot.Features.Ipo.ApplyIpo;

public enum ApplyStep { Accounts, Ipos }

public sealed record PendingApply(
    ApplyStep Step,
    IReadOnlyList<MeroShareUser> Users,
    IReadOnlyList<IpoData> Ipos,
    IReadOnlyList<MeroShareUser>? SelectedUsers = null);

// Singleton — owns the multi-step keyboard state across webhook requests.
public sealed class PendingApplyStore
{
    private readonly ConcurrentDictionary<long, PendingApply> _store = new();

    public void Set(long chatId, PendingApply pending) => _store[chatId] = pending;

    public PendingApply? Get(long chatId) => _store.TryGetValue(chatId, out var pending) ? pending : null;

    public void Remove(long chatId) => _store.TryRemove(chatId, out _);
}
