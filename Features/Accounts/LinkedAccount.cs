namespace MeroShareBot.Features.Accounts;

// Persisted per chat. Password/Crn/Pin are ciphertext (CryptoService). Username/Dp stay plaintext
// (needed for list rendering and DP-list validation, neither is a secret).
public sealed record LinkedAccount(
    Guid Id,
    string Username,
    string Dp,
    string EncryptedPassword,
    string? EncryptedCrn,
    string? EncryptedPin,
    bool AutoApplyEnabled,
    int? AutoApplyKitta,
    HashSet<int> AutoApplyPromptedShareIds,
    HashSet<int> AppliedShareIds,
    DateTimeOffset LinkedAt);

public sealed record ChatAccounts(
    long ChatId,
    List<LinkedAccount> Accounts,
    Guid? DefaultAccountId,
    bool NotifyEnabled);
