using System.Collections.Concurrent;
using System.Text.Json;

namespace MeroShareBot.Features.Accounts;

// Singleton, JSON-file-backed (data/accounts.json) — same Load/Save/lock pattern as PendingApplyStore,
// keyed by chat with a list of linked accounts instead of one account per chat.
public sealed class AccountStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly CryptoService _crypto;
    private readonly ILogger<AccountStore> _logger;
    private readonly ConcurrentDictionary<long, ChatAccounts> _byChat = new();
    private readonly object _saveLock = new();

    public AccountStore(IHostEnvironment env, CryptoService crypto, ILogger<AccountStore> logger)
    {
        _crypto = crypto;
        _logger = logger;
        _path = Path.Combine(env.ContentRootPath, "data", "accounts.json");
        Load();
    }

    private void Load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (!File.Exists(_path)) return;

        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<long, ChatAccounts>>(File.ReadAllText(_path));
            if (loaded is null) return;
            foreach (var (chatId, accounts) in loaded) _byChat[chatId] = accounts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load {Path}", _path);
        }
    }

    private void Save()
    {
        lock (_saveLock)
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_byChat.ToDictionary(kv => kv.Key, kv => kv.Value), Json));
        }
    }

    public IReadOnlyList<LinkedAccount> GetAccounts(long chatId) =>
        _byChat.TryGetValue(chatId, out var c) ? c.Accounts : [];

    public LinkedAccount? GetAccount(long chatId, int index1Based)
    {
        var accounts = GetAccounts(chatId);
        return index1Based >= 1 && index1Based <= accounts.Count ? accounts[index1Based - 1] : null;
    }

    public LinkedAccount? GetAccountById(long chatId, Guid accountId) =>
        GetAccounts(chatId).FirstOrDefault(a => a.Id == accountId);

    // System-wide: the same MeroShare account can't be linked under more than one chat.
    public bool IsUsernameLinked(string username, string dp) =>
        _byChat.Values.Any(c => c.Accounts.Any(a =>
            string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Dp, dp, StringComparison.OrdinalIgnoreCase)));

    public int? AddAccount(long chatId, string username, string dp, string password, string? crn, string? pin)
    {
        lock (_saveLock)
        {
            if (IsUsernameLinked(username, dp)) return null;

            var account = new LinkedAccount(
                Guid.NewGuid(), username, dp,
                _crypto.Encrypt(password),
                string.IsNullOrEmpty(crn) ? null : _crypto.Encrypt(crn),
                string.IsNullOrEmpty(pin) ? null : _crypto.Encrypt(pin),
                AutoApplyEnabled: false, AutoApplyKitta: null,
                AutoApplyPromptedShareIds: [], AppliedShareIds: [],
                DateTimeOffset.UtcNow);

            var chat = _byChat.GetOrAdd(chatId, id => new ChatAccounts(id, [], null, NotifyEnabled: false));
            chat.Accounts.Add(account);
            if (chat.DefaultAccountId is null) chat = chat with { DefaultAccountId = account.Id };
            _byChat[chatId] = chat;
            Save();
            return chat.Accounts.Count;
        }
    }

    public bool RemoveAccount(long chatId, int index1Based)
    {
        if (!_byChat.TryGetValue(chatId, out var chat)) return false;
        if (index1Based < 1 || index1Based > chat.Accounts.Count) return false;

        var removed = chat.Accounts[index1Based - 1];
        chat.Accounts.RemoveAt(index1Based - 1);
        var newDefault = chat.DefaultAccountId == removed.Id
            ? chat.Accounts.FirstOrDefault()?.Id
            : chat.DefaultAccountId;
        _byChat[chatId] = chat with { DefaultAccountId = newDefault };
        Save();
        return true;
    }

    public bool SetDefault(long chatId, int index1Based)
    {
        var account = GetAccount(chatId, index1Based);
        if (account is null) return false;
        _byChat[chatId] = _byChat[chatId] with { DefaultAccountId = account.Id };
        Save();
        return true;
    }

    // Falls back to the sole account if the chat never explicitly set a default.
    public LinkedAccount? GetDefault(long chatId)
    {
        var accounts = GetAccounts(chatId);
        if (accounts.Count == 0) return null;
        if (accounts.Count == 1) return accounts[0];
        return _byChat.TryGetValue(chatId, out var chat) && chat.DefaultAccountId is { } id
            ? accounts.FirstOrDefault(a => a.Id == id)
            : null;
    }

    public bool SetAutoApply(long chatId, int index1Based, bool enabled, int? kitta) =>
        MutateAccount(chatId, index1Based, a => a with { AutoApplyEnabled = enabled, AutoApplyKitta = kitta ?? a.AutoApplyKitta });

    public bool SetChatNotify(long chatId, bool enabled)
    {
        var chat = _byChat.GetOrAdd(chatId, id => new ChatAccounts(id, [], null, false));
        _byChat[chatId] = chat with { NotifyEnabled = enabled };
        Save();
        return true;
    }

    public bool GetChatNotify(long chatId) => _byChat.TryGetValue(chatId, out var chat) && chat.NotifyEnabled;

    public bool MarkAutoApplyPrompted(long chatId, Guid accountId, int companyShareId) =>
        MutateAccountById(chatId, accountId, a => { a.AutoApplyPromptedShareIds.Add(companyShareId); return a; });

    public bool MarkApplied(long chatId, Guid accountId, int companyShareId) =>
        MutateAccountById(chatId, accountId, a => { a.AppliedShareIds.Add(companyShareId); return a; });

    public IReadOnlyList<long> GetNotifyEnabledChatIds() =>
        [.. _byChat.Values.Where(c => c.NotifyEnabled).Select(c => c.ChatId)];

    public IReadOnlyList<(long ChatId, LinkedAccount Account)> GetAutoApplyEnabledAccounts() =>
        [.. _byChat.Values.SelectMany(c => c.Accounts.Where(a => a.AutoApplyEnabled).Select(a => (c.ChatId, a)))];

    // Any one linked account anywhere, purely to get a session for the account-agnostic issue list.
    public (long ChatId, LinkedAccount Account)? GetAnyAccountForIssueListing()
    {
        foreach (var chat in _byChat.Values)
        {
            var account = chat.Accounts.FirstOrDefault();
            if (account is not null) return (chat.ChatId, account);
        }
        return null;
    }

    public DecryptedAccount Decrypt(LinkedAccount a) => new(
        new MeroShareCredentials(a.Username, _crypto.Decrypt(a.EncryptedPassword), a.Dp),
        a.EncryptedCrn is null ? "" : _crypto.Decrypt(a.EncryptedCrn),
        a.EncryptedPin is null ? "" : _crypto.Decrypt(a.EncryptedPin));

    private bool MutateAccount(long chatId, int index1Based, Func<LinkedAccount, LinkedAccount> mutate)
    {
        if (!_byChat.TryGetValue(chatId, out var chat)) return false;
        if (index1Based < 1 || index1Based > chat.Accounts.Count) return false;
        chat.Accounts[index1Based - 1] = mutate(chat.Accounts[index1Based - 1]);
        Save();
        return true;
    }

    private bool MutateAccountById(long chatId, Guid id, Func<LinkedAccount, LinkedAccount> mutate)
    {
        if (!_byChat.TryGetValue(chatId, out var chat)) return false;
        var index = chat.Accounts.FindIndex(a => a.Id == id);
        if (index < 0) return false;
        chat.Accounts[index] = mutate(chat.Accounts[index]);
        Save();
        return true;
    }
}
