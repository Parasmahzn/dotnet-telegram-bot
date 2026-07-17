using System.Net;
using System.Net.Http.Headers;

namespace MeroShareBot.Shared.MeroShare;

// Central registration point for every HttpClient that talks to CDSC's backend, so
// MeroShareApiClient / MeroShareDpCatalog / MeroShareSessionCache stay identically configured.
public static class MeroShareHttpClientExtensions
{
    // Matches a real browser's request shape (captured via Playwright against meroshare.cdsc.com.np) —
    // MeroShare's edge WAF has intermittently blocked requests missing these.
    public static void ConfigureMeroShareClient(IServiceProvider sp, HttpClient http)
    {
        http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<MeroShareOptions>>().Value.BaseUrl + "/");
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.Referrer = new Uri("https://meroshare.cdsc.com.np/");
        http.DefaultRequestHeaders.Add("Origin", "https://meroshare.cdsc.com.np");

        // Prefer HTTP/1.1 — CDSC's edge/WAF intermittently rejects .NET HTTP/2 fingerprints on /auth.
        http.DefaultRequestVersion = HttpVersion.Version11;
        http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
    }

    // Cookie jar + Set-Cookie can trip the WAF on subsequent /auth POSTs.
    public static SocketsHttpHandler ConfigureMeroSharePrimaryHandler() => new()
    {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
    };

    // Wires the primary handler above plus dev-only request/response logging onto a client builder.
    public static IHttpClientBuilder AddMeroShareHandlers(this IHttpClientBuilder builder) =>
        builder.ConfigurePrimaryHttpMessageHandler(ConfigureMeroSharePrimaryHandler)
            .AddHttpMessageHandler<MeroShareLoggingHandler>();
}
