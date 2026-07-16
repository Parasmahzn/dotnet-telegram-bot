namespace MeroShareBot.Shared.Users;

public sealed record UserRecord(
    long ChatId,
    string FirstName,
    string LastName,
    string Username,
    DateTimeOffset RegisteredAt,
    bool IsAdmin = false,
    bool IsApplyAllowed = false,
    bool IsBlocked = false);
