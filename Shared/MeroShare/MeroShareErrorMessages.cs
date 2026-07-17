using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace MeroShareBot.Shared.MeroShare;

/// <summary>
/// Central place for turning any exception thrown from a MeroShare-authenticated call into a message
/// safe to show an end user. <see cref="MeroShareApiException.ApiMessage"/> and
/// <see cref="MeroShareAccountStatusException"/>'s message are backend-authored and user-safe.
/// <see cref="MeroShareLoginException"/>'s message is internal diagnostic text, so — like any other
/// unrecognized exception — it is logged at Error and replaced with <paramref name="genericFallback"/>
/// instead of ever reaching the user.
/// </summary>
public static class MeroShareErrorMessages
{
    /// <summary>
    /// Resolves <paramref name="ex"/> to a user-safe message, logging it first when it's not one of
    /// the known user-safe MeroShare exception types.
    /// </summary>
    /// <param name="operation">Short label for the Error log line, e.g. "Login validation", "applyIPO".</param>
    /// <param name="genericFallback">Shown to the user when there's no safe, specific reason available.</param>
    public static string Resolve(this Exception ex, ILogger logger, string operation, string genericFallback) => ex switch
    {
        MeroShareApiException { ApiMessage: { } apiMessage } => apiMessage,
        MeroShareAccountStatusException => ex.Message,
        MeroShareUnavailableException => ex.Message,
        _ => LogAndFallback(ex, logger, operation, genericFallback),
    };

    private static string LogAndFallback(Exception ex, ILogger logger, string operation, string fallback)
    {
        logger.LogError(ex, "{Operation} failed", operation);
        return fallback;
    }
}
