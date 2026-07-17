using MeroShareBot.Shared.Data.Entities;

namespace MeroShareBot.Shared.Users;

// Singleton — backed by MySQL via a short-lived BotDbContext per call (IDbContextFactory).
// Registration registry only: no MeroShare credentials live here, those stay in AccountStore.
public sealed class UserStore(IDbContextFactory<BotDbContext> factory)
{
    public void RegisterUser(long chatId, string firstName, string lastName, string username)
    {
        using var db = factory.CreateDbContext();
        var existing = db.Users.Find(chatId);

        if (existing is null)
        {
            db.Users.Add(new UserEntity
            {
                ChatId = chatId,
                FirstName = firstName,
                LastName = lastName,
                Username = username,
                RegisteredAt = DateTimeOffset.UtcNow,
                IsAdmin = false,
                IsApplyAllowed = false,
                IsBlocked = false,
            });
        }
        else
        {
            if (existing.FirstName == firstName && existing.LastName == lastName && existing.Username == username) return;
            existing.FirstName = firstName;
            existing.LastName = lastName;
            existing.Username = username;
        }

        db.SaveChanges();
    }

    public IReadOnlyList<long> GetAllChatIds()
    {
        using var db = factory.CreateDbContext();
        return [.. db.Users.Select(u => u.ChatId)];
    }

    public UserRecord? GetUser(long chatId)
    {
        using var db = factory.CreateDbContext();
        var u = db.Users.Find(chatId);
        return u is null ? null : new UserRecord(u.ChatId, u.FirstName, u.LastName, u.Username, u.RegisteredAt, u.IsAdmin, u.IsApplyAllowed, u.IsBlocked);
    }

    public bool IsAdmin(long chatId) => GetUser(chatId)?.IsAdmin == true;

    public bool IsBlocked(long chatId) => GetUser(chatId)?.IsBlocked == true;

    public bool IsApplyAllowed(long chatId) => GetUser(chatId)?.IsApplyAllowed == true;

    public IReadOnlyList<UserRecord> GetAllUsers()
    {
        using var db = factory.CreateDbContext();
        return [.. db.Users.Select(u =>
            new UserRecord(u.ChatId, u.FirstName, u.LastName, u.Username, u.RegisteredAt, u.IsAdmin, u.IsApplyAllowed, u.IsBlocked))];
    }

    public bool SetAdmin(long chatId, bool value) => SetFlag(chatId, e => e.IsAdmin = value);

    public bool SetApplyAllowed(long chatId, bool value) => SetFlag(chatId, e => e.IsApplyAllowed = value);

    public bool SetBlocked(long chatId, bool value) => SetFlag(chatId, e => e.IsBlocked = value);

    private bool SetFlag(long chatId, Action<UserEntity> apply)
    {
        using var db = factory.CreateDbContext();
        var entity = db.Users.Find(chatId);
        if (entity is null) return false;
        apply(entity);
        db.SaveChanges();
        return true;
    }
}
