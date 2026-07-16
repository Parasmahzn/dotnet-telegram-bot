namespace MeroShareBot.Features.Ipo.ApplyIpo;

public sealed record ApplyResult(bool Success, string? Message = null, string? Error = null);

public sealed record AccountApplyResult(string Username, bool Success, string? Message = null, string? Error = null);

public sealed class ApplyIpoHandler(MeroShareApiClient client, ILogger<ApplyIpoHandler> logger)
{
    public async Task<ApplyResult> ApplyAsync(
        MeroShareCredentials creds, MeroShareApplyCredentials applyCreds, IpoData ipo, int kitta)
    {
        try
        {
            var session = await client.LoginAsync(creds);
            try
            {
                var detail = await client.GetIssueDetailAsync(session, ipo.CompanyShareId);
                if (kitta < detail.MinUnit || (detail.MaxUnit > 0 && kitta > detail.MaxUnit))
                    return new ApplyResult(false, Error: $"Kitta {kitta} outside allowed range [{detail.MinUnit}-{detail.MaxUnit}]");

                var ownDetail = await client.GetOwnDetailAsync(session);
                var banks = await client.GetBanksAsync(session);
                var bank = banks.FirstOrDefault();
                if (bank is null) return new ApplyResult(false, Error: "No bank found on this MeroShare account.");

                var accounts = await client.GetBankAccountsAsync(session, bank.Id);
                var bankAccount = accounts.FirstOrDefault();
                if (bankAccount is null) return new ApplyResult(false, Error: $"No account found for bank {bank.Name}.");

                var apply = new ApplyRequest(
                    Demat: ownDetail.Demat,
                    Boid: ownDetail.Boid,
                    AccountNumber: bankAccount.AccountNumber,
                    CustomerId: bankAccount.Id,
                    AccountBranchId: bankAccount.AccountBranchId,
                    AccountTypeId: bankAccount.AccountTypeId,
                    AppliedKitta: kitta.ToString(),
                    CrnNumber: applyCreds.Crn,
                    TransactionPin: applyCreds.Pin,
                    CompanyShareId: ipo.CompanyShareId.ToString(),
                    BankId: bank.Id.ToString());

                var response = await client.ApplyAsync(session, apply);
                return new ApplyResult(true, Message: response.Message ?? $"Applied for {ipo.Name}");
            }
            finally
            {
                try { await client.LogoutAsync(session); } catch { /* best-effort */ }
            }
        }
        catch (MeroShareApiException ex)
        {
            return new ApplyResult(false, Error: ex.ApiMessage ?? ex.Message);
        }
        catch (MeroShareLoginException ex)
        {
            return new ApplyResult(false, Error: ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "applyIPO failed");
            return new ApplyResult(false, Error: ex.Message);
        }
    }
}
