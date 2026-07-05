using Microsoft.Playwright;

namespace MeroShareBot.Shared.Browser;

public sealed class BrowserFactory(IHostEnvironment env) : IAsyncDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IPlaywright? _playwright;

    public async Task<T> WithPageAsync<T>(Func<IPage, Task<T>> action)
    {
        var playwright = await GetPlaywrightAsync();
        var isDev = env.IsDevelopment();

        var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = !isDev,
            SlowMo = isDev ? 300 : 0,
        });

        try
        {
            var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            return await action(page);
        }
        finally
        {
            // Dev parity with the Node bot: leave the headed browser open for inspection.
            if (!isDev) await browser.CloseAsync();
        }
    }

    private async Task<IPlaywright> GetPlaywrightAsync()
    {
        if (_playwright is not null) return _playwright;
        await _initLock.WaitAsync();
        try
        {
            return _playwright ??= await Playwright.CreateAsync();
        }
        finally
        {
            _initLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _playwright?.Dispose();
        _initLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
