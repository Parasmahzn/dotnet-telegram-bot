using MeroShareBot.Shared.MeroShare;
using Microsoft.Playwright;

namespace MeroShareBot.Features.Ipo;

// Shared #/asba page navigation used by both the list and apply flows (port of src/meroshare/ipo.js).
internal static class AsbaNavigation
{
    public static async Task NavigateToAsbaAsync(IPage page, string baseUrl)
    {
        await page.GotoAsync($"{baseUrl}#/asba", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForTimeoutAsync(1000);
    }

    public static async Task ClickTabAsync(IPage page, string tabText)
    {
        var tab = page.Locator($"text=\"{tabText}\"").First;
        if (await PlaywrightHelpers.IsVisibleWithinAsync(tab, 3000)) await tab.ClickAsync();
        await page.WaitForTimeoutAsync(800);
    }

    // Race: IPO list vs empty-state — whichever appears first wins.
    public static async Task<bool> WaitForIpoListAsync(IPage page)
    {
        var list = AsOutcome(
            page.WaitForSelectorAsync(".company-list", new() { Timeout = 8000 }),
            "list");
        var empty = AsOutcome(
            page.Locator("text=No Record(s) Found")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 8000 }),
            "empty");

        var state = await await Task.WhenAny(list, empty);
        return state == "list";
    }

    private static async Task<string?> AsOutcome(Task task, string value)
    {
        try
        {
            await task;
            return value;
        }
        catch
        {
            return null;
        }
    }
}
