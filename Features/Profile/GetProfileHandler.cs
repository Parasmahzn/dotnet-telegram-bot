namespace MeroShareBot.Features.Profile;

public sealed class GetProfileHandler(MeroShareApiClient client, ILogger<GetProfileHandler> logger)
{
    public async Task<ProfileResult> HandleAsync(MeroShareCredentials creds, Func<string, Task> notify)
    {
        try
        {
            await notify($"📋 Retrieving account details for {creds.Username}...");
            var detail = await client.GetOwnDetailAsync(creds);

            return new ProfileResult(
                true,
                Personal: new ProfilePersonal(
                    Boid: detail.Boid,
                    Name: detail.Name ?? "",
                    Gender: detail.Gender ?? "",
                    Email: detail.Email ?? detail.MeroShareEmail ?? "",
                    Phone: detail.Contact ?? "",
                    Address: detail.Address ?? ""),
                Account:
                [
                    new ProfileAccountEntry("Demat", detail.Demat),
                    new ProfileAccountEntry("Demat Expiry Date", detail.DematExpiryDate ?? ""),
                    new ProfileAccountEntry("MeroShare Expiry Date", detail.ExpiredDateStr ?? ""),
                    new ProfileAccountEntry("Password Expiry Date", detail.PasswordExpiryDateStr ?? ""),
                ]);
        }
        catch (Exception ex)
        {
            return new ProfileResult(false, Error: $"❌ {ex.Resolve(logger, "Profile fetch", "Login failed — check credentials.")}");
        }
    }
}
