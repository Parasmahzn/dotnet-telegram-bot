using MeroShareBot.Shared.Browser;
using MeroShareBot.Shared.Config;
using MeroShareBot.Shared.MeroShare;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace MeroShareBot.Features.Profile;

// Port of getProfile from src/meroshare/profile.js.
public sealed class GetProfileHandler(
    BrowserFactory browser,
    LoginService login,
    IOptions<MeroShareOptions> opts,
    ILogger<GetProfileHandler> logger)
{
    public async Task<ProfileResult> HandleAsync(MeroShareUser user, Func<string, Task> notify)
    {
        var config = opts.Value;

        return await browser.WithPageAsync(async page =>
        {
            try
            {
                await notify($"🔐 Signing in as {user.Username}...");
                await page.GotoAsync(config.LoginUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
                await login.PerformLoginAsync(page, user);

                // Check if login actually succeeded (still on login page = failed)
                if (page.Url.Contains("#/login"))
                {
                    var pageError = await login.ScrapePageErrorAsync(page);
                    return new ProfileResult(false, Error: $"❌ {pageError ?? "Login failed — check credentials."}");
                }

                await notify($"📋 Retrieving account details for {user.Username}...");
                await page.GotoAsync($"{config.BaseUrl}#/ownProfile", new() { WaitUntil = WaitUntilState.NetworkIdle });
                await page.WaitForSelectorAsync("span.editable-form-block__text", new() { Timeout = 8000 });

                // Personal info DOM order: BOID(0), Name(1), Gender(2), Email(3), Phone(4), Address(5)
                var personalValues = await page.Locator("span.editable-form-block__text").AllTextContentsAsync();
                var accountLabels = await page.Locator(".account-info__text").AllTextContentsAsync();
                var accountDates = await page.Locator(".account-info__date").AllTextContentsAsync();
                var accountUsername = (await page.Locator("p.username").First.TextContentAsync())?.Trim();

                return new ProfileResult(
                    true,
                    Personal: new ProfilePersonal(
                        Boid: At(personalValues, 0),
                        Name: At(personalValues, 1),
                        Gender: At(personalValues, 2),
                        Email: At(personalValues, 3),
                        Phone: At(personalValues, 4),
                        Address: At(personalValues, 5),
                        Username: accountUsername),
                    Account:
                    [
                        .. accountLabels.Select((label, i) => new ProfileAccountEntry(label.Trim(), At(accountDates, i))),
                    ]);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Profile fetch failed");
                return new ProfileResult(false, Error: ex.Message);
            }
        });
    }

    private static string At(IReadOnlyList<string> values, int index) =>
        index < values.Count ? values[index].Trim() : "";
}
