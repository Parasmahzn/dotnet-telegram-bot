namespace MeroShareBot.Shared.Data.Entities;

// DB row shape for a linked MeroShare account. Kept separate from Features.Accounts.LinkedAccount
// (the domain record used throughout the rest of the app) so persistence concerns — ChatId as a
// real column, JSON-serialized share-id sets — never leak into feature code.
public sealed class LinkedAccountEntity
{
    public Guid Id { get; set; }
    public long ChatId { get; set; }
    public required string Username { get; set; }
    public required string Dp { get; set; }
    public required string Label { get; set; }
    public required string EncryptedPassword { get; set; }
    public string? EncryptedCrn { get; set; }
    public string? EncryptedPin { get; set; }
    public bool AutoApplyEnabled { get; set; }
    public int? AutoApplyKitta { get; set; }
    public HashSet<int> AutoApplyPromptedShareIds { get; set; } = [];
    public HashSet<int> AppliedShareIds { get; set; } = [];
    public DateTimeOffset LinkedAt { get; set; }

    // Plaintext (unlike Password/Crn/Pin) per explicit decision — reused for Postman testing.
    public string? SessionToken { get; set; }
    public DateTimeOffset? SessionTokenExpiresAt { get; set; }
}
