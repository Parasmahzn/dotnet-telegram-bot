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
            var detail = await client.GetIssueDetailAsync(creds, ipo.CompanyShareId);
            if (kitta < detail.MinUnit || (detail.MaxUnit > 0 && kitta > detail.MaxUnit))
                return new ApplyResult(false, Error: $"Kitta {kitta} outside allowed range [{detail.MinUnit}-{detail.MaxUnit}]");

            var ownDetail = await client.GetOwnDetailAsync(creds);
            var banks = await client.GetBanksAsync(creds);
            var bank = banks.FirstOrDefault();
            if (bank is null) return new ApplyResult(false, Error: "No bank found on this MeroShare account.");

            var accounts = await client.GetBankAccountsAsync(creds, bank.Id);
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

            var response = await client.ApplyAsync(creds, apply);
            return new ApplyResult(true, Message: response.Message ?? $"Applied for {ipo.Name}");
        }
        catch (Exception ex)
        {
            return new ApplyResult(false, Error: ex.Resolve(logger, "applyIPO", "Login failed — check credentials."));
        }
    }
}
