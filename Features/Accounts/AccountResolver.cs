namespace MeroShareBot.Features.Accounts;

// Single shared [n]/default resolution used by /profile, /portfolio, /ipo, /apply.
public sealed class AccountResolver(AccountStore store)
{
    // No arg + only one linked account: unambiguous, use it directly.
    // No arg + multiple linked accounts: null forces the caller to prompt (picker or account-agnostic fallback).
    public LinkedAccount? Resolve(long chatId, string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            var accounts = store.GetAccounts(chatId);
            return accounts.Count == 1 ? accounts[0] : null;
        }
        return int.TryParse(arg, out var index) ? store.GetAccount(chatId, index) : null;
    }
}
