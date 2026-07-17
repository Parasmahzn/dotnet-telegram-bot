namespace MeroShareBot.Shared.MeroShare;

// Immutable. Obtained via IMeroShareSessionCache, which caches/persists it for reuse across
// operations until MeroShare rejects it (401) or its JWT expiry passes — see MeroShareSessionCache.
// Token is the raw Authorization response-header value from login, no "Bearer " prefix.
public sealed record MeroShareSession(string Token, int ClientId, string Username);
