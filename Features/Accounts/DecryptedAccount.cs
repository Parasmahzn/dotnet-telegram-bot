namespace MeroShareBot.Features.Accounts;

public sealed record DecryptedAccount(MeroShareCredentials Credentials, string Crn, string Pin)
{
    public MeroShareApplyCredentials ApplyCredentials => new(Crn, Pin);
}
