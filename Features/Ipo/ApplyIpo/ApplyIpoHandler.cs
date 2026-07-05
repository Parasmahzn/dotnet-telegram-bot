using MeroShareBot.Shared.Browser;
using MeroShareBot.Shared.Config;
using MeroShareBot.Shared.MeroShare;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace MeroShareBot.Features.Ipo.ApplyIpo;

public sealed record ApplyResult(bool Success, string? Message = null, string? Error = null);

public sealed record AccountApplyResult(string Username, bool Success, string? Message = null, string? Error = null);

// Port of applyIPO / applyIPOAllAccounts from src/meroshare/ipo.js.
public sealed class ApplyIpoHandler(
    BrowserFactory browser,
    LoginService login,
    IOptions<MeroShareOptions> opts,
    ILogger<ApplyIpoHandler> logger)
{
    private static readonly string[] KittaSelectors =
    [
        "input[placeholder*='kitta' i]",
        "input[placeholder*='unit' i]",
        "input[placeholder*='quantity' i]",
        "input[type='number']",
        "input[name*='kitta' i]",
    ];

    private static readonly string[] CrnSelectors =
    [
        "input[formcontrolname*='crn' i]",
        "input[placeholder*='CRN' i]",
        "input[name*='crn' i]",
        "input[id*='crn' i]",
    ];

    private static readonly string[] PinSelectors =
    [
        "input[formcontrolname*='transactionPin' i]",
        "input[formcontrolname*='pin' i]",
        "input[placeholder*='PIN' i]",
        "input[type='password']",
    ];

    public async Task<IReadOnlyList<AccountApplyResult>> ApplyAllAccountsAsync(
        string ipoName, int kitta, IReadOnlyList<MeroShareUser> users, Func<string, Task> notify)
    {
        var results = new List<AccountApplyResult>();
        foreach (var user in users)
        {
            await notify($"⚙️ Applying with account {user.Username}...");
            var result = await ApplyAsync(user, ipoName, kitta);
            results.Add(new AccountApplyResult(user.Username, result.Success, result.Message, result.Error));
            await notify(result.Success
                ? $"✅ Applied successfully with {user.Username}"
                : $"❌ Failed for {user.Username}: {result.Error}");
        }
        return results;
    }

    public async Task<ApplyResult> ApplyAsync(MeroShareUser user, string ipoName, int kitta)
    {
        var config = opts.Value;

        return await browser.WithPageAsync(async page =>
        {
            try
            {
                await page.GotoAsync(config.LoginUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
                await login.PerformLoginAsync(page, user);
                if (page.Url.Contains("#/login"))
                {
                    var pageError = await login.ScrapePageErrorAsync(page);
                    return new ApplyResult(false, Error: pageError ?? "Login failed — check credentials.");
                }

                await AsbaNavigation.NavigateToAsbaAsync(page, config.BaseUrl);
                await AsbaNavigation.ClickTabAsync(page, "Apply for Issue");

                if (!await AsbaNavigation.WaitForIpoListAsync(page))
                    return new ApplyResult(false, Error: $"IPO \"{ipoName}\" not found — no open issues");

                var rows = await page.QuerySelectorAllAsync(".company-list");
                IElementHandle? targetRow = null;
                foreach (var row in rows)
                {
                    var text = await row.TextContentAsync() ?? "";
                    if (text.Contains(ipoName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetRow = row;
                        break;
                    }
                }
                if (targetRow is null)
                    return new ApplyResult(false, Error: $"IPO \"{ipoName}\" not found in open issues");

                var applyBtn = await targetRow.QuerySelectorAsync("button");
                if (applyBtn is null) return new ApplyResult(false, Error: "Apply button not found");
                await applyBtn.ClickAsync();
                await page.WaitForTimeoutAsync(1500);

                // Kitta quantity
                await FillFirstVisibleInputAsync(page, KittaSelectors, kitta.ToString());

                // CRN number
                if (!string.IsNullOrEmpty(user.Crn))
                    await FillFirstVisibleInputAsync(page, CrnSelectors, user.Crn);

                // Transaction PIN
                if (!string.IsNullOrEmpty(user.Pin))
                    await FillFirstVisibleInputAsync(page, PinSelectors, user.Pin);

                // Submit
                var submitBtn = page.Locator("button[type='submit'], button:has-text('Apply'), button:has-text('Submit')").First;
                if (await PlaywrightHelpers.IsVisibleWithinAsync(submitBtn, 2000))
                {
                    await submitBtn.ClickAsync();
                    await page.WaitForTimeoutAsync(2000);
                }

                var successMsg = page.Locator("text=/success/i, text=/applied/i, .alert-success").First;
                if (await PlaywrightHelpers.IsVisibleWithinAsync(successMsg, 3000))
                    return new ApplyResult(true, Message: (await successMsg.TextContentAsync())?.Trim());

                return new ApplyResult(true, Message: $"Applied for {ipoName}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "applyIPO failed");
                return new ApplyResult(false, Error: ex.Message);
            }
        });
    }

    // Fill the first visible input from a fallback selector chain, skipping silently if none appear.
    private static async Task FillFirstVisibleInputAsync(IPage page, IReadOnlyList<string> selectors, string value)
    {
        foreach (var selector in selectors)
        {
            var input = page.Locator(selector).First;
            if (await PlaywrightHelpers.IsVisibleWithinAsync(input, 1000))
            {
                await input.FillAsync(value);
                return;
            }
        }
    }
}
