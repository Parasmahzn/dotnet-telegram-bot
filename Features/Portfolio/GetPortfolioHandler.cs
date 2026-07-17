namespace MeroShareBot.Features.Portfolio;

public sealed class GetPortfolioHandler(MeroShareApiClient client, ILogger<GetPortfolioHandler> logger)
{
    public async Task<PortfolioResult> HandleAsync(MeroShareCredentials creds, Func<string, Task> notify)
    {
        try
        {
            var own = await client.GetOwnDetailAsync(creds);
            await notify("📊 Fetching holdings...");
            var portfolio = await client.GetPortfolioAsync(creds, own.Demat, own.ClientCode);
            logger.LogDebug("Fetched portfolio for {Username}: {Count} holdings", creds.Username, portfolio.Items.Count);

            return new PortfolioResult(true, portfolio);
        }
        catch (Exception ex)
        {
            return new PortfolioResult(false, Error: $"❌ {ex.Resolve(logger, "Portfolio fetch", "Login failed — check credentials.")}");
        }
    }
}
