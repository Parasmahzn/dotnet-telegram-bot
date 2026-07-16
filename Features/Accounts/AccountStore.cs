using Microsoft.EntityFrameworkCore;
using MeroShareBot.Shared.Data;
using MeroShareBot.Shared.Data.Entities;
using MySqlConnector;

namespace MeroShareBot.Features.Accounts;

// Singleton — backed by MySQL via a short-lived BotDbContext per call (IDbContextFactory), so the
// registration stays a singleton without sharing a DbContext instance across concurrent chats.
public sealed class AccountStore(IDbContextFactory<BotDbContext> factory, CryptoService crypto)
{
    public IReadOnlyList<LinkedAccount> GetAccounts(long chatId)
    {
        using var db = factory.CreateDbContext();
        return [.. db.LinkedAccounts.Where(a => a.ChatId == chatId).OrderBy(a => a.LinkedAt).AsEnumerable().Select(ToRecord)];
    }

    public LinkedAccount? GetAccount(long chatId, int index1Based)
    {
        var accounts = GetAccounts(chatId);
        return index1Based >= 1 && index1Based <= accounts.Count ? accounts[index1Based - 1] : null;
    }

    public LinkedAccount? GetAccountById(long chatId, Guid accountId)
    {
        using var db = factory.CreateDbContext();
        var entity = db.LinkedAccounts.FirstOrDefault(a => a.ChatId == chatId && a.Id == accountId);
        return entity is null ? null : ToRecord(entity);
    }

    // System-wide: the same MeroShare account can't be linked under more than one chat.
    public bool IsUsernameLinked(string username, string dp)
    {
        using var db = factory.CreateDbContext();
        return db.LinkedAccounts.Any(a => a.Username == username && a.Dp == dp);
    }

    public int? AddAccount(long chatId, string username, string dp, string password, string? crn, string? pin)
    {
        using var db = factory.CreateDbContext();

        var entity = new LinkedAccountEntity
        {
            Id = Guid.NewGuid(),
            ChatId = chatId,
            Username = username,
            Dp = dp,
            EncryptedPassword = crypto.Encrypt(password),
            EncryptedCrn = string.IsNullOrEmpty(crn) ? null : crypto.Encrypt(crn),
            EncryptedPin = string.IsNullOrEmpty(pin) ? null : crypto.Encrypt(pin),
            AutoApplyEnabled = false,
            AutoApplyKitta = null,
            AutoApplyPromptedShareIds = [],
            AppliedShareIds = [],
            LinkedAt = DateTimeOffset.UtcNow,
        };
        db.LinkedAccounts.Add(entity);

        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
        {
            return null;
        }

        var settings = db.ChatSettings.Find(chatId);
        if (settings is null)
        {
            db.ChatSettings.Add(new ChatSettingsEntity { ChatId = chatId, DefaultAccountId = entity.Id, NotifyEnabled = false });
            db.SaveChanges();
        }

        return db.LinkedAccounts.Count(a => a.ChatId == chatId);
    }

    public bool RemoveAccount(long chatId, int index1Based)
    {
        using var db = factory.CreateDbContext();
        var accounts = db.LinkedAccounts.Where(a => a.ChatId == chatId).OrderBy(a => a.LinkedAt).ToList();
        if (index1Based < 1 || index1Based > accounts.Count) return false;

        var removed = accounts[index1Based - 1];
        db.LinkedAccounts.Remove(removed);

        var settings = db.ChatSettings.Find(chatId);
        if (settings is not null && settings.DefaultAccountId == removed.Id)
        {
            settings.DefaultAccountId = accounts.Where(a => a.Id != removed.Id).Select(a => (Guid?)a.Id).FirstOrDefault();
        }

        db.SaveChanges();
        return true;
    }

    public bool SetDefault(long chatId, int index1Based)
    {
        var account = GetAccount(chatId, index1Based);
        if (account is null) return false;

        using var db = factory.CreateDbContext();
        var settings = db.ChatSettings.Find(chatId);
        if (settings is null)
        {
            db.ChatSettings.Add(new ChatSettingsEntity { ChatId = chatId, DefaultAccountId = account.Id, NotifyEnabled = false });
        }
        else
        {
            settings.DefaultAccountId = account.Id;
        }
        db.SaveChanges();
        return true;
    }

    // Falls back to the sole account if the chat never explicitly set a default.
    public LinkedAccount? GetDefault(long chatId)
    {
        var accounts = GetAccounts(chatId);
        if (accounts.Count == 0) return null;
        if (accounts.Count == 1) return accounts[0];

        using var db = factory.CreateDbContext();
        var defaultId = db.ChatSettings.Find(chatId)?.DefaultAccountId;
        return defaultId is { } id ? accounts.FirstOrDefault(a => a.Id == id) : null;
    }

    public bool SetAutoApply(long chatId, int index1Based, bool enabled, int? kitta) =>
        MutateAccount(chatId, index1Based, a =>
        {
            a.AutoApplyEnabled = enabled;
            a.AutoApplyKitta = kitta ?? a.AutoApplyKitta;
        });

    public bool SetChatNotify(long chatId, bool enabled)
    {
        using var db = factory.CreateDbContext();
        var settings = db.ChatSettings.Find(chatId);
        if (settings is null)
        {
            db.ChatSettings.Add(new ChatSettingsEntity { ChatId = chatId, DefaultAccountId = null, NotifyEnabled = enabled });
        }
        else
        {
            settings.NotifyEnabled = enabled;
        }
        db.SaveChanges();
        return true;
    }

    public bool GetChatNotify(long chatId)
    {
        using var db = factory.CreateDbContext();
        return db.ChatSettings.Find(chatId)?.NotifyEnabled == true;
    }

    public bool MarkAutoApplyPrompted(long chatId, Guid accountId, int companyShareId) =>
        MutateAccountById(chatId, accountId, a => a.AutoApplyPromptedShareIds.Add(companyShareId));

    public bool MarkApplied(long chatId, Guid accountId, int companyShareId) =>
        MutateAccountById(chatId, accountId, a => a.AppliedShareIds.Add(companyShareId));

    public IReadOnlyList<long> GetNotifyEnabledChatIds()
    {
        using var db = factory.CreateDbContext();
        return [.. db.ChatSettings.Where(c => c.NotifyEnabled).Select(c => c.ChatId)];
    }

    public IReadOnlyList<(long ChatId, LinkedAccount Account)> GetAutoApplyEnabledAccounts()
    {
        using var db = factory.CreateDbContext();
        return [.. db.LinkedAccounts.Where(a => a.AutoApplyEnabled).AsEnumerable().Select(a => (a.ChatId, ToRecord(a)))];
    }

    // Any one linked account anywhere, purely to get a session for the account-agnostic issue list.
    public (long ChatId, LinkedAccount Account)? GetAnyAccountForIssueListing()
    {
        using var db = factory.CreateDbContext();
        var entity = db.LinkedAccounts.FirstOrDefault();
        return entity is null ? null : (entity.ChatId, ToRecord(entity));
    }

    public DecryptedAccount Decrypt(LinkedAccount a) => new(
        new MeroShareCredentials(a.Username, crypto.Decrypt(a.EncryptedPassword), a.Dp),
        a.EncryptedCrn is null ? "" : crypto.Decrypt(a.EncryptedCrn),
        a.EncryptedPin is null ? "" : crypto.Decrypt(a.EncryptedPin));

    private bool MutateAccount(long chatId, int index1Based, Action<LinkedAccountEntity> mutate)
    {
        using var db = factory.CreateDbContext();
        var accounts = db.LinkedAccounts.Where(a => a.ChatId == chatId).OrderBy(a => a.LinkedAt).ToList();
        if (index1Based < 1 || index1Based > accounts.Count) return false;
        mutate(accounts[index1Based - 1]);
        db.SaveChanges();
        return true;
    }

    private bool MutateAccountById(long chatId, Guid id, Action<LinkedAccountEntity> mutate)
    {
        using var db = factory.CreateDbContext();
        var account = db.LinkedAccounts.FirstOrDefault(a => a.ChatId == chatId && a.Id == id);
        if (account is null) return false;
        mutate(account);
        db.SaveChanges();
        return true;
    }

    private static LinkedAccount ToRecord(LinkedAccountEntity e) => new(
        e.Id, e.Username, e.Dp, e.EncryptedPassword, e.EncryptedCrn, e.EncryptedPin,
        e.AutoApplyEnabled, e.AutoApplyKitta, e.AutoApplyPromptedShareIds, e.AppliedShareIds, e.LinkedAt);
}
