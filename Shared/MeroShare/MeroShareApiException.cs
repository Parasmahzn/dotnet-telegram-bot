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

// Raised when POST /meroShare/auth/ returns 2xx but no Authorization response header — a contract
// violation distinct from bad credentials, so callers can message it differently.
public sealed class MeroShareLoginException(string reason) : Exception(reason);
