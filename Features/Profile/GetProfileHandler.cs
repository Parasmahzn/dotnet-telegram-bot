namespace MeroShareBot.Features.Profile;

public sealed class GetProfileHandler(MeroShareApiClient client, ILogger<GetProfileHandler> logger)
{
    public async Task<ProfileResult> HandleAsync(MeroShareCredentials creds, Func<string, Task> notify)
    {
        try
        {
            await notify($"🔐 Signing in as {creds.Username}...");
            var session = await client.LoginAsync(creds);

            await notify($"📋 Retrieving account details for {creds.Username}...");
            var detail = await client.GetOwnDetailAsync(session);
            try { await client.LogoutAsync(session); } catch { /* best-effort */ }

            return new ProfileResult(
                true,
                Personal: new ProfilePersonal(
                    Boid: detail.Boid,
                    Name: detail.Name ?? "",
                    Gender: detail.Gender ?? "",
                    Email: detail.Email ?? detail.MeroShareEmail ?? "",
                    Phone: detail.Contact ?? "",
                    Address: detail.Address ?? "",
                    Username: detail.Username),
                Account:
                [
                    new ProfileAccountEntry("Demat", detail.Demat),
                    new ProfileAccountEntry("Demat Expiry Date", detail.DematExpiryDate ?? ""),
                ]);
        }
        catch (MeroShareApiException ex)
        {
            return new ProfileResult(false, Error: $"❌ {ex.ApiMessage ?? "Login failed — check credentials."}");
        }
        catch (MeroShareLoginException ex)
        {
            return new ProfileResult(false, Error: $"❌ {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Profile fetch failed");
            return new ProfileResult(false, Error: ex.Message);
        }
    }
}
