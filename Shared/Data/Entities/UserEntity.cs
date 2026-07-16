namespace MeroShareBot.Shared.Data.Entities;

// Chat registration registry row. No MeroShare credentials live here — those stay in LinkedAccountEntity.
public sealed class UserEntity
{
    public long ChatId { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Username { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsApplyAllowed { get; set; }
    public bool IsBlocked { get; set; }
}
