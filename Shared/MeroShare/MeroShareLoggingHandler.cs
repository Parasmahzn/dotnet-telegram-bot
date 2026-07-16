namespace MeroShareBot.Shared.MeroShare;

// Dev-only verbose HTTP logging — replaces the old "headed browser to watch what's happening"
// debugging affordance now that there's no browser. No-ops in Production.
public sealed class MeroShareLoggingHandler(IHostEnvironment env, ILogger<MeroShareLoggingHandler> logger)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!env.IsDevelopment()) return await base.SendAsync(request, ct);

        var reqBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        logger.LogDebug("MeroShare API -> {Method} {Uri} {Body}", request.Method, request.RequestUri, reqBody);

        var response = await base.SendAsync(request, ct);

        var resBody = await response.Content.ReadAsStringAsync(ct);
        logger.LogDebug("MeroShare API <- {Status} {Body}", (int)response.StatusCode, resBody);

        return response;
    }
}
