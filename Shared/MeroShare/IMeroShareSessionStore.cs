namespace MeroShareBot.Shared.MeroShare;

// Port implemented by Features/Accounts/AccountStore — keeps MeroShareSessionCache (Shared) from
// depending on account storage directly, same DIP shape as IMeroShareDpCatalog.
public interface IMeroShareSessionStore
{
    Task<(string Token, DateTimeOffset? ExpiresAt)?> GetAsync(string username, string dp, CancellationToken ct = default);

    Task SaveAsync(string username, string dp, string token, DateTimeOffset? expiresAt, CancellationToken ct = default);

    Task ClearAsync(string username, string dp, CancellationToken ct = default);
}
