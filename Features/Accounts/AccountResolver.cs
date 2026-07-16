namespace MeroShareBot.Features.Accounts;

// Single shared [n]/default resolution used by /profile, /portfolio, /ipo, /apply.
public sealed class AccountResolver(AccountStore store)
{
    public LinkedAccount? Resolve(long chatId, string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return store.GetDefault(chatId);
        return int.TryParse(arg, out var index) ? store.GetAccount(chatId, index) : null;
    }
}
