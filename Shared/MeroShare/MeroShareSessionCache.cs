using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace MeroShareBot.Shared.MeroShare;

public interface IMeroShareSessionCache
{
    Task<MeroShareSession> GetSessionAsync(MeroShareCredentials creds, CancellationToken ct = default);

    Task InvalidateAsync(MeroShareCredentials creds, CancellationToken ct = default);
}

// Owns the actual login HTTP call (moved here from MeroShareApiClient.LoginAsync) plus caching and
// DB persistence, so MeroShareApiClient can depend on this instead of the other way around — no
// circular dependency. Registered as a genuine singleton (named HttpClient + IHttpClientFactory,
// NOT AddHttpClient<T> which defaults to transient) since the per-account lock dictionary below
// must survive across requests — the same transient-typed-client trap already hit once this
// session with MeroShareDpCatalog's cache.
public sealed class MeroShareSessionCache(
    IHttpClientFactory httpClientFactory,
    IMeroShareDpCatalog dpCatalog,
    IMeroShareSessionStore store,
    IMemoryCache cache,
    ILogger<MeroShareSessionCache> logger) : IMeroShareSessionCache
{
    public const string HttpClientName = nameof(MeroShareSessionCache);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly JwtSecurityTokenHandler JwtHandler = new();

    // MeroShare's token is a real JWT (confirmed via jwt.io) with an exp claim, but this fallback
    // stays as a defensive backstop in case that ever changes — a 401 from a real call is always
    // the authoritative "expired" signal regardless of which path set the expiry.
    private static readonly TimeSpan SafetyNetTtl = TimeSpan.FromHours(12);

    private readonly ConcurrentDictionary<(string Username, string Dp), SemaphoreSlim> _locks = new();

    public async Task<MeroShareSession> GetSessionAsync(MeroShareCredentials creds, CancellationToken ct = default)
    {
        var cacheKey = CacheKey(creds);
        if (cache.TryGetValue(cacheKey, out MeroShareSession? cached) && cached is not null)
        {
            logger.LogDebug("Session cache hit for {Username}", creds.Username);
            return cached;
        }

        var sem = _locks.GetOrAdd((creds.Username, creds.Dp), _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(cacheKey, out cached) && cached is not null)
                return cached; // another request already refreshed it while we waited

            var clientId = await dpCatalog.ResolveClientIdAsync(creds.Dp, ct);

            var persisted = await store.GetAsync(creds.Username, creds.Dp, ct);
            if (persisted is { } p && (p.ExpiresAt is null || p.ExpiresAt > DateTimeOffset.UtcNow))
            {
                logger.LogDebug("Session db hit for {Username}", creds.Username);
                var fromDb = new MeroShareSession(p.Token, clientId, creds.Username);
                cache.Set(cacheKey, fromDb, CacheOptionsFor(p.ExpiresAt));
                return fromDb;
            }

            logger.LogInformation("Logging in fresh for {Username}", creds.Username);
            return await LoginAsync(creds, clientId, ct);
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task InvalidateAsync(MeroShareCredentials creds, CancellationToken ct = default)
    {
        logger.LogDebug("Invalidating session for {Username}", creds.Username);
        cache.Remove(CacheKey(creds));
        await store.ClearAsync(creds.Username, creds.Dp, ct);
    }

    private async Task<MeroShareSession> LoginAsync(MeroShareCredentials creds, int clientId, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "meroShare/auth/")
        {
            Content = JsonContent.Create(new LoginRequest(clientId, creds.Username, creds.Password), options: Json),
        };
        using var response = await http.SendAsync(request, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var parsed = TryDeserialize<LoginResponseBody>(body);
            throw new MeroShareApiException((int)response.StatusCode, body, parsed?.Message);
        }

        if (!response.Headers.TryGetValues("Authorization", out var values))
            throw new MeroShareLoginException("Login succeeded but no Authorization header was returned.");

        var token = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
            throw new MeroShareLoginException("Login succeeded but the Authorization header was empty.");

        var parsedBody = TryDeserialize<LoginResponseBody>(body);
        if (parsedBody is { } b && (b.DematExpired == true || b.AccountExpired == true || b.PasswordExpired == true))
            throw new MeroShareAccountStatusException(b.Message ?? "MeroShare account requires attention — please log in via the website.");

        var expiresAt = TryGetJwtExpiry(token);
        await store.SaveAsync(creds.Username, creds.Dp, token, expiresAt, ct);

        var session = new MeroShareSession(token, clientId, creds.Username);
        cache.Set(CacheKey(creds), session, CacheOptionsFor(expiresAt));
        return session;
    }

    private static DateTimeOffset? TryGetJwtExpiry(string token)
    {
        try
        {
            if (!JwtHandler.CanReadToken(token)) return null;
            var jwt = JwtHandler.ReadJwtToken(token);
            return jwt.ValidTo == default ? null : new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero) - TimeSpan.FromMinutes(2);
        }
        catch
        {
            return null;
        }
    }

    private static MemoryCacheEntryOptions CacheOptionsFor(DateTimeOffset? expiresAt) =>
        new() { AbsoluteExpiration = expiresAt ?? DateTimeOffset.UtcNow + SafetyNetTtl };

    private static string CacheKey(MeroShareCredentials creds) => $"session:{creds.Username}:{creds.Dp}";

    private static T? TryDeserialize<T>(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, Json);
        }
        catch
        {
            return default;
        }
    }
}
