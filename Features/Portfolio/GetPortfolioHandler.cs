namespace MeroShareBot.Features.Portfolio;

public sealed class GetPortfolioHandler(MeroShareApiClient client, ILogger<GetPortfolioHandler> logger)
{
    public async Task<PortfolioResult> HandleAsync(MeroShareCredentials creds, Func<string, Task> notify)
    {
        try
        {
            await notify($"🔐 Signing in as {creds.Username}...");
            var session = await client.LoginAsync(creds);

            var own = await client.GetOwnDetailAsync(session);
            await notify("📊 Fetching holdings...");
            var portfolio = await client.GetPortfolioAsync(session, own.Demat, own.ClientCode);
            try { await client.LogoutAsync(session); } catch { /* best-effort */ }

            return new PortfolioResult(true, portfolio);
        }
        catch (MeroShareApiException ex)
        {
            return new PortfolioResult(false, Error: $"❌ {ex.ApiMessage ?? "Login failed — check credentials."}");
        }
        catch (MeroShareLoginException ex)
        {
            return new PortfolioResult(false, Error: $"❌ {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Portfolio fetch failed");
            return new PortfolioResult(false, Error: ex.Message);
        }
    }
}
