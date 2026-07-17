namespace MeroShareBot.Features.Accounts;

// Persisted per chat. Password/Crn/Pin are ciphertext (CryptoService). Username/Dp stay plaintext
// (needed for list rendering and DP-list validation, neither is a secret).
public sealed record LinkedAccount(
    Guid Id,
    string Username,
    string Dp,
    string Label,
    string EncryptedPassword,
    string? EncryptedCrn,
    string? EncryptedPin,
    bool AutoApplyEnabled,
    int? AutoApplyKitta,
    HashSet<int> AutoApplyPromptedShareIds,
    HashSet<int> AppliedShareIds,
    DateTimeOffset LinkedAt)
{
    // Existing pre-label accounts have Label == "" — fall back to Username rather than retroactively
    // assigning "Account N" numbers to old rows.
    public string DisplayLabel => string.IsNullOrEmpty(Label) ? Username : Label;
}
