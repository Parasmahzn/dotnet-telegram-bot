using MeroShareBot.Shared.Config;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using static MeroShareBot.Shared.MeroShare.PlaywrightHelpers;

namespace MeroShareBot.Shared.MeroShare;

// Port of src/meroshare/login.js — selector arrays and DP strategies copied verbatim.
public sealed class LoginService(IOptions<MeroShareOptions> opts)
{
    private static readonly string[] PageReadySelectors =
    [
        "span.select2-container",
        "ng-select",
        "select",
        "form",
    ];

    private static readonly string[] UsernameSelectors =
    [
        "input#username",
        "input[name='username']",
        "input[name='email']",
        "input[type='text']",
        "input[id*='user']",
        "input[placeholder*='username' i]",
        "input[placeholder*='email' i]",
    ];

    private static readonly string[] PasswordSelectors =
    [
        "input#password",
        "input[name='password']",
        "input[type='password']",
        "input[id*='pass']",
    ];

    private static readonly string[] LoginButtonSelectors =
    [
        "button[type='submit']",
        "button:has-text('Login')",
        "button:has-text('Sign in')",
        "button:has-text('Log in')",
        "button:has-text('LOGIN')",
        "input[type='submit']",
        "button.btn-primary",
        "button.btn-login",
    ];

    // Selectors for error/toast messages shown by the SPA after a failed action.
    private static readonly string[] ErrorSelectors =
    [
        ".alert-danger",
        ".toast-error",
        ".swal2-html-container",
        "[class*='error' i]:visible",
        "[class*='alert' i]:visible",
    ];

    // Each strategy describes how to interact with a specific dropdown type.
    // Add new dropdown types here without touching SelectDpAsync logic (Open/Closed).
    // OptionSelectors == null means a native <select> handled via SelectOptionAsync.
    private sealed record DpStrategy(string[] ContainerSelectors, Func<string, string[]>? OptionSelectors = null);

    private static readonly DpStrategy[] DpStrategies =
    [
        // Select2 custom dropdown
        new(
            [
                "span.select2-container:has-text('Select your DP')",
                "span.select2-selection:has-text('Select your DP')",
                "span.select2-selection__rendered:has-text('Select your DP')",
                "#select2-dk1h-container",
                "span.select2-container",
            ],
            dp =>
            [
                $"li.select2-results__option:has-text(\"{dp}\")",
                $"ul.select2-results__options li:has-text(\"{dp}\")",
                $"li:has-text(\"{dp}\")",
                $"text=\"{dp}\"",
            ]),
        // Native HTML <select> element
        new(
            [
                "select#selectBranch",
                "select[name*='dp' i]",
                "select[id*='dp' i]",
                "select",
            ]),
        // Angular ng-select and other custom dropdowns
        new(
            [
                "ng-select .ng-select-container",
                "ng-select",
                "[aria-haspopup='listbox']",
                "div:has-text('Select your DP')",
                "div:has-text('Select DP')",
                "label:has-text('DP') + *",
            ],
            dp =>
            [
                $"[role=\"option\"]:has-text(\"{dp}\")",
                $".ng-option:has-text(\"{dp}\")",
                $"li:has-text(\"{dp}\")",
                $"div[class*=\"option\"]:has-text(\"{dp}\")",
                $"text=\"{dp}\"",
            ]),
    ];

    public async Task PerformLoginAsync(IPage page, MeroShareUser user)
    {
        if (!string.IsNullOrEmpty(user.Dp)) await SelectDpAsync(page, user.Dp);
        await FillLoginFormAsync(page, user);
        await ClickLoginButtonAsync(page);
        await page.WaitForTimeoutAsync(2000);
    }

    public async Task<string?> ScrapePageErrorAsync(IPage page)
    {
        foreach (var selector in ErrorSelectors)
        {
            try
            {
                var el = page.Locator(selector).First;
                if (await IsVisibleWithinAsync(el, 800))
                {
                    var text = (await el.TextContentAsync())?.Trim();
                    if (!string.IsNullOrEmpty(text)) return text;
                }
            }
            catch
            {
                // try the next selector
            }
        }
        return null;
    }

    public async Task<(string? FullName, string? Username)> ExtractUserInfoAsync(IPage page)
    {
        await page.GotoAsync($"{opts.Value.BaseUrl}#/ownProfile", new() { WaitUntil = WaitUntilState.NetworkIdle });
        string? fullName = null, username = null;
        try
        {
            await page.WaitForSelectorAsync("p.username", new() { Timeout = 5000 });
            username = (await page.Locator("p.username").First.TextContentAsync())?.Trim();
            fullName = (await page.Locator("span.editable-form-block__text").Nth(1).TextContentAsync())?.Trim();
        }
        catch
        {
            // profile details are best-effort
        }
        return (fullName, username);
    }

    private static async Task<bool> SelectDpAsync(IPage page, string dpName)
    {
        await WaitForPageReadyAsync(page, PageReadySelectors, 10000);
        await page.WaitForTimeoutAsync(1000);

        foreach (var strategy in DpStrategies)
        {
            var container = await FindFirstVisibleAsync(page, strategy.ContainerSelectors);
            if (container is null) continue;

            if (strategy.OptionSelectors is null)
            {
                try
                {
                    await container.SelectOptionAsync(new SelectOptionValue { Label = dpName });
                    await page.WaitForTimeoutAsync(500);
                    return true;
                }
                catch
                {
                    continue;
                }
            }

            // Open the dropdown then click the matching option
            await container.ClickAsync();
            await page.WaitForTimeoutAsync(1000);

            var option = await FindFirstVisibleAsync(page, strategy.OptionSelectors(dpName));
            if (option is not null)
            {
                await option.ClickAsync();
                await page.WaitForTimeoutAsync(500);
                return true;
            }
        }

        return false;
    }

    private static async Task FillLoginFormAsync(IPage page, MeroShareUser user)
    {
        await WaitForPageReadyAsync(page, UsernameSelectors, 10000);
        await page.WaitForTimeoutAsync(500);
        await FillFirstVisibleAsync(page, UsernameSelectors, user.Username);
        await FillFirstVisibleAsync(page, PasswordSelectors, user.Password);
    }

    private static async Task ClickLoginButtonAsync(IPage page)
    {
        var clicked = await ClickFirstVisibleAsync(page, LoginButtonSelectors, 1000);
        if (!clicked) throw new InvalidOperationException("Could not find login button");
    }
}
