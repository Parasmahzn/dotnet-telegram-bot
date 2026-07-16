namespace MeroShareBot.Features.Ipo.GetOpenIpos;

public sealed class GetOpenIposHandler(MeroShareApiClient client, ILogger<GetOpenIposHandler> logger)
{
    public async Task<IReadOnlyList<IpoData>> HandleAsync(MeroShareCredentials creds, Func<string, Task>? notify = null)
    {
        var send = notify ?? (_ => Task.CompletedTask);
        try
        {
            await send($"🔐 Signing in as {creds.Username}...");
            var session = await client.LoginAsync(creds);

            await send("🔍 Checking open IPOs...");
            var response = await client.GetApplicableIssuesAsync(session);
            try { await client.LogoutAsync(session); } catch { /* best-effort */ }

            return [.. response.Object.Select(ToIpoData)];
        }
        catch (MeroShareLoginException ex)
        {
            await send($"❌ {ex.Message}");
            return [];
        }
        catch (MeroShareApiException ex)
        {
            await send($"❌ {ex.ApiMessage ?? "Login failed — check credentials."}");
            return [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "getOpenIPOs failed");
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
