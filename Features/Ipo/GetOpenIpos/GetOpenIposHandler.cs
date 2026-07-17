namespace MeroShareBot.Features.Ipo.GetOpenIpos;

public sealed class GetOpenIposHandler(MeroShareApiClient client, ILogger<GetOpenIposHandler> logger)
{
    public async Task<IReadOnlyList<IpoData>> HandleAsync(MeroShareCredentials creds, Func<string, Task>? notify = null)
    {
        var send = notify ?? (_ => Task.CompletedTask);
        try
        {
            await send("🔍 Checking open IPOs...");
            var response = await client.GetApplicableIssuesAsync(creds);

            return [.. response.Object.Select(ToIpoData)];
        }
        catch (Exception ex)
        {
            await send($"❌ {ex.Resolve(logger, "getOpenIPOs", "Login failed — check credentials.")}");
            return [];
        }
    }

    private static IpoData ToIpoData(ApplicableIssue issue) => new(
        CompanyShareId: issue.CompanyShareId,
        Name: issue.CompanyName,
        Symbol: issue.Scrip,
        SubGroup: issue.SubGroup,
        Type: issue.ShareTypeName,
        ShareType: issue.ShareGroupName);
}
