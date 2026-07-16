using System.Text.Json;

namespace MeroShareBot.Shared.MeroShare;

public interface IMeroShareDpCatalog
{
    Task<IReadOnlyList<DepositoryParticipant>> GetDpListAsync(CancellationToken ct = default);

    Task<int> ResolveClientIdAsync(string dp, CancellationToken ct = default);
}

// DPs are effectively static reference data — cached with a long TTL so every login doesn't pay
// for an extra round trip. Match by Code first, then Name, to handle both the numeric-code and
// full-bank-name Dp values already present in existing config/linked accounts.
public sealed class MeroShareDpCatalog(HttpClient http, TimeProvider timeProvider, ILogger<MeroShareDpCatalog> logger)
    : IMeroShareDpCatalog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(12);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyList<DepositoryParticipant>? _cache;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public async Task<int> ResolveClientIdAsync(string dp, CancellationToken ct = default)
    {
        var list = await GetDpListAsync(ct);

        var match = list.FirstOrDefault(d => string.Equals(d.Code, dp, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(d => string.Equals(d.Name, dp, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(d => d.Name.Contains(dp, StringComparison.OrdinalIgnoreCase));

        return match?.Id
            ?? throw new MeroShareLoginException($"No matching Depository Participant found for \"{dp}\".");
    }

    public async Task<IReadOnlyList<DepositoryParticipant>> GetDpListAsync(CancellationToken ct = default)
    {
        if (_cache is { } cached && timeProvider.GetUtcNow() < _expiresAt) return cached;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (_cache is { } stillCached && timeProvider.GetUtcNow() < _expiresAt) return stillCached;

            using var response = await http.GetAsync("meroShare/capital/", ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new MeroShareApiException((int)response.StatusCode, body);

            List<DepositoryParticipant> list;
            try
            {
                list = JsonSerializer.Deserialize<List<DepositoryParticipant>>(body, Json) ?? [];
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "MeroShare DP list returned non-JSON body (status {Status}): {Body}",
                    (int)response.StatusCode, body.Length > 300 ? body[..300] + "…" : body);
                throw new MeroShareApiException((int)response.StatusCode, body);
            }

            _cache = list;
            _expiresAt = timeProvider.GetUtcNow() + Ttl;
            return list;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
