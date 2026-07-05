using Microsoft.Playwright;

namespace MeroShareBot.Shared.MeroShare;

// Ports of src/playwright/utils.js — the multi-selector fallback pattern used throughout.
public static class PlaywrightHelpers
{
    // "Is this element visible within N ms?" — replaces the deprecated IsVisibleAsync timeout option.
    public static async Task<bool> IsVisibleWithinAsync(ILocator locator, float timeoutMs)
    {
        try
        {
            await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<ILocator?> FindFirstVisibleAsync(IPage page, IReadOnlyList<string> selectors, float timeoutMs = 2000)
    {
        foreach (var selector in selectors)
        {
            var el = page.Locator(selector).First;
            if (await IsVisibleWithinAsync(el, timeoutMs)) return el;
        }
        return null;
    }

    public static async Task FillFirstVisibleAsync(IPage page, IReadOnlyList<string> selectors, string value, float timeoutMs = 2000)
    {
        var el = await FindFirstVisibleAsync(page, selectors, timeoutMs)
            ?? throw new InvalidOperationException($"No visible input found among: {string.Join(", ", selectors)}");
        await el.ClearAsync();
        await el.FillAsync(value);
    }

    public static async Task<bool> ClickFirstVisibleAsync(IPage page, IReadOnlyList<string> selectors, float timeoutMs = 2000)
    {
        var el = await FindFirstVisibleAsync(page, selectors, timeoutMs);
        if (el is null) return false;
        await el.ClickAsync();
        return true;
    }

    // Waits for any one selector to appear (replaces waitForNetworkIdle on MeroShare's SPA).
    public static async Task WaitForPageReadyAsync(IPage page, IReadOnlyList<string> selectors, float timeoutMs = 10000)
    {
        foreach (var selector in selectors)
        {
            try
            {
                await page.WaitForSelectorAsync(selector, new() { Timeout = timeoutMs });
                return;
            }
            catch
            {
                // try the next selector
            }
        }

        try
        {
            await page.WaitForLoadStateAsync(LoadState.Load, new() { Timeout = 5000 });
        }
        catch
        {
            await page.WaitForTimeoutAsync(1000);
        }
    }
}
