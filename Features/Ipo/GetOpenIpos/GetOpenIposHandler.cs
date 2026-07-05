using System.Text.Json;
using MeroShareBot.Shared.Browser;
using MeroShareBot.Shared.Config;
using MeroShareBot.Shared.MeroShare;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace MeroShareBot.Features.Ipo.GetOpenIpos;

public sealed class GetOpenIposHandler(
    BrowserFactory browser,
    LoginService login,
    IOptions<MeroShareOptions> opts,
    ILogger<GetOpenIposHandler> logger)
{
    // JS copied verbatim from the Node scraper (src/meroshare/ipo.js).
    private const string ScrapeRowsJs =
        """
        rows => rows.map(row => {
            const spans = [...row.querySelectorAll('span')]
                .map(s => s.textContent.replace(/\s+/g, ' ').trim())
                .filter(Boolean);
            // spans: ["CompanyName", "-", "For General Public (SYM)", "IPO", "ShareType"]
            const subGroup = spans[2] ?? '';
            const symMatch = subGroup.match(/\(([^)]+)\)/);
            return {
                name:      spans[0] ?? '',
                symbol:    symMatch ? symMatch[1] : '',
                subGroup,
                type:      row.querySelector('span.share-of-type')?.textContent.trim() ?? '',
                shareType: row.querySelector('span.isin')?.textContent.trim() ?? spans[4] ?? '',
            };
        })
        """;

    public async Task<IReadOnlyList<IpoData>> HandleAsync(MeroShareUser user, Func<string, Task>? notify = null)
    {
        var send = notify ?? (_ => Task.CompletedTask);
        var config = opts.Value;

        return await browser.WithPageAsync<IReadOnlyList<IpoData>>(async page =>
        {
            try
            {
                await send($"🔐 Signing in as {user.Username}...");
                await page.GotoAsync(config.LoginUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
                await login.PerformLoginAsync(page, user);
                if (page.Url.Contains("#/login"))
                {
                    var pageError = await login.ScrapePageErrorAsync(page);
                    await send($"❌ {pageError ?? "Login failed — check credentials."}");
                    return [];
                }

                await send("🔍 Checking open IPOs...");
                await AsbaNavigation.NavigateToAsbaAsync(page, config.BaseUrl);
                await AsbaNavigation.ClickTabAsync(page, "Apply for Issue");

                if (!await AsbaNavigation.WaitForIpoListAsync(page)) return [];

                var rows = await page.Locator(".company-list").EvaluateAllAsync<JsonElement>(ScrapeRowsJs);
                return [.. rows.EnumerateArray().Select(ParseRow)];
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "getOpenIPOs failed");
                return [];
            }
        });
    }

    private static IpoData ParseRow(JsonElement row) => new(
        Name: GetString(row, "name"),
        Symbol: GetString(row, "symbol"),
        SubGroup: GetString(row, "subGroup"),
        Type: GetString(row, "type"),
        ShareType: GetString(row, "shareType"));

    private static string GetString(JsonElement el, string property) =>
        el.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
