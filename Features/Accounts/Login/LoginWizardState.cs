using System.Collections.Concurrent;

namespace MeroShareBot.Features.Accounts.Login;

public sealed record FieldPrompt(string Key, string Prompt, bool Optional);

public sealed class WizardSession
{
    public required Dictionary<string, string> Collected { get; init; }
    public required Queue<FieldPrompt> Steps { get; init; }

    // Distinguishes the initial field-collection phase from the label phase that follows a
    // successful login validation — both reuse the same dequeue-loop in LoginEndpoint.
    public bool AwaitingLabel { get; init; } = false;
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

    // Separate from FieldPrompts — only asked after a real successful login, never part of the
    // upfront queue (a failed login must never reach this step).
    public static readonly FieldPrompt LabelPrompt = new("Label",
        "🏷️ Almost done!\n\nSend a label for this account (e.g. Personal, Father, Wife).\n\n" +
        "This label helps you identify multiple MeroShare accounts later.\n\n" +
        "Type \"skip\" to use the default label.\nSend /cancel to abort.",
        true);

    private readonly ConcurrentDictionary<long, WizardSession> _sessions = new();

    public bool HasPending(long chatId) => _sessions.ContainsKey(chatId);

    public void Start(long chatId, WizardSession session) => _sessions[chatId] = session;

    public WizardSession? Get(long chatId) => _sessions.GetValueOrDefault(chatId);

    public void Clear(long chatId) => _sessions.TryRemove(chatId, out _);
}
