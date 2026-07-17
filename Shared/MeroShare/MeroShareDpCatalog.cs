using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace MeroShareBot.Shared.MeroShare;

public interface IMeroShareDpCatalog
{
    Task<IReadOnlyList<DepositoryParticipant>> GetDpListAsync(CancellationToken ct = default);

    Task<int> ResolveClientIdAsync(string dp, CancellationToken ct = default);
}

// DPs are effectively static reference data — cached for 30 days via IMemoryCache (a DI singleton)
// so every login doesn't pay for an extra round trip. Match by Code first, then Name, to handle
// both the numeric-code and full-bank-name Dp values already present in existing config/linked accounts.
public sealed class MeroShareDpCatalog(HttpClient http, IMemoryCache cache, ILogger<MeroShareDpCatalog> logger)
    : IMeroShareDpCatalog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);
    private const string CacheKey = "MeroShareDpCatalog:DpList";

    public async Task<int> ResolveClientIdAsync(string dp, CancellationToken ct = default)
    {
        var list = await GetDpListAsync(ct);

        var match = list.FirstOrDefault(d => string.Equals(d.Code, dp, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(d => string.Equals(d.Name, dp, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(d => d.Name.Contains(dp, StringComparison.OrdinalIgnoreCase));

        return match?.Id
            ?? throw new MeroShareLoginException($"No matching Depository Participant found for \"{dp}\".");
    }

    public async Task<IReadOnlyList<DepositoryParticipant>> GetDpListAsync(CancellationToken ct = default) =>
        (await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;

            using var response = await http.GetAsync("meroShare/capital/", ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new MeroShareApiException((int)response.StatusCode, body);

            try
            {
                return JsonSerializer.Deserialize<List<DepositoryParticipant>>(body, Json) ?? [];
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "MeroShare DP list returned non-JSON body (status {Status}): {Body}",
                    (int)response.StatusCode, body.Length > 300 ? body[..300] + "…" : body);
                throw new MeroShareUnavailableException();
            }
        }))!;
}
