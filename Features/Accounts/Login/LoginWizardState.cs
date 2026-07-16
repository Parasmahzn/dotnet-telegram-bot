using System.Collections.Concurrent;

namespace MeroShareBot.Features.Accounts.Login;

public sealed record FieldPrompt(string Key, string Prompt, bool Optional);

public sealed class WizardSession
{
    public required Dictionary<string, string> Collected { get; init; }
    public required Queue<FieldPrompt> Steps { get; init; }
}

// Singleton — owns the /login conversational state across webhook requests, same pattern as
// PendingApplyStore.
public sealed class LoginWizardState
{
    public static readonly IReadOnlyList<FieldPrompt> FieldPrompts =
    [
        new("Username", "What is your MeroShare username?", false),
        new("Dp", "What is your DP (Depository Participant)? Send the DP code or name shown on MeroShare's login page.", false),
        new("Password", "What is your MeroShare password?", false),
        new("Crn", "What is your CRN Number? (type \"skip\" to leave blank)", true),
        new("Pin", "What is your Transaction PIN? (type \"skip\" to leave blank)", true),
    ];

    private readonly ConcurrentDictionary<long, WizardSession> _sessions = new();

    public bool HasPending(long chatId) => _sessions.ContainsKey(chatId);

    public void Start(long chatId, WizardSession session) => _sessions[chatId] = session;

    public WizardSession? Get(long chatId) => _sessions.GetValueOrDefault(chatId);

    public void Clear(long chatId) => _sessions.TryRemove(chatId, out _);
}
