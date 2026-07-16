namespace MeroShareBot.Shared.MeroShare;

// Immutable, short-lived — obtained fresh from LoginAsync for one logical operation and discarded.
// Token is the raw Authorization response-header value from login, no "Bearer " prefix.
public sealed record MeroShareSession(string Token, int ClientId, string Username);
