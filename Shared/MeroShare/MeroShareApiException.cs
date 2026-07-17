namespace MeroShareBot.Shared.MeroShare;

// Thrown for any non-2xx response from the MeroShare backend. ApiMessage is the parsed "message"
// field from the response body when present (e.g. bad-credentials login failures, apply rejections).
public sealed class MeroShareApiException(int statusCode, string body, string? apiMessage = null)
    : Exception(apiMessage ?? $"MeroShare API returned {statusCode}: {Truncate(body)}")
{
    public int StatusCode { get; } = statusCode;
    public string Body { get; } = body;
    public string? ApiMessage { get; } = apiMessage;

    private static string Truncate(string s) => s.Length > 300 ? s[..300] + "…" : s;
}

/// <summary>
/// Raised when POST /meroShare/auth/ returns 2xx but the response doesn't conform to the expected
/// login contract (missing or empty Authorization header). This indicates an unexpected API/integration
/// problem, not a credentials or account issue — <see cref="Exception.Message"/> is diagnostic text and
/// must never be shown to end users.
/// </summary>
public sealed class MeroShareLoginException(string reason) : Exception(reason);

/// <summary>
/// Raised when POST /meroShare/auth/ returns 2xx with a valid Authorization header, but the response body
/// flags the account itself as unusable (demat/account/password expired). <see cref="Exception.Message"/>
/// is MeroShare's own account-status reason (or a safe fallback) and is safe to show directly to end users.
/// </summary>
public sealed class MeroShareAccountStatusException(string reason) : Exception(reason);

/// <summary>
/// Raised when a MeroShare response returns 2xx but the body isn't valid JSON — MeroShare's edge WAF
/// occasionally blocks a request this way (an HTML "Request Rejected" page) rather than returning a
/// proper error status. <see cref="Exception.Message"/> is a fixed, safe-to-show message; the real
/// cause is a WAF/infrastructure issue, not anything the end user can fix by re-entering credentials.
/// </summary>
public sealed class MeroShareUnavailableException() : Exception(
    "Unable to retrieve data from MeroShare.\n\n" +
    "The remote server rejected the request.\n\n" +
    "Please try again in a few minutes. If this continues to happen, the MeroShare service may be temporarily unavailable.");
