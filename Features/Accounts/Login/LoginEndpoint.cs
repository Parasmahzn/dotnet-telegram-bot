namespace MeroShareBot.Features.Accounts.Login;

public sealed class LoginEndpoint(
    LoginWizardState state,
    AccountStore accounts,
    MeroShareApiClient client,
    TelegramSender sender,
    ILogger<LoginEndpoint> logger)
{
    public async Task HandleAsync(Message msg)
    {
        var chatId = msg.Chat.Id;
        var steps = new Queue<FieldPrompt>(LoginWizardState.FieldPrompts);
        state.Start(chatId, new WizardSession { Collected = [], Steps = steps });
        await sender.SendTextAsync(chatId,
            "🔗 Let's link a MeroShare account.\n(Type \"cancel\" anytime to stop.)\n\n" + steps.Peek().Prompt);
    }

    public async Task HandleFreeTextAsync(Message msg)
    {
        var chatId = msg.Chat.Id;
        var session = state.Get(chatId);
        if (session is null) return;

        var raw = (msg.Text ?? "").Trim();
        if (raw.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            state.Clear(chatId);
            await sender.SendTextAsync(chatId, "❌ Login cancelled.");
            return;
        }

        var step = session.Steps.Dequeue();
        session.Collected[step.Key] = step.Optional && raw.Equals("skip", StringComparison.OrdinalIgnoreCase) ? "" : raw;

        if (session.Steps.Count > 0)
        {
            await sender.SendTextAsync(chatId, session.Steps.Peek().Prompt);
            return;
        }

        state.Clear(chatId);
        await FinishAsync(chatId, session.Collected);
    }

    private async Task FinishAsync(long chatId, Dictionary<string, string> c)
    {
        var username = c["Username"];
        var dpInput = c["Dp"];
        var password = c["Password"];
        var crn = c.GetValueOrDefault("Crn", "");
        var pin = c.GetValueOrDefault("Pin", "");

        await sender.SendTextAsync(chatId, "🔎 Validating DP code...");
        List<DepositoryParticipant> dpList;
        try
        {
            dpList = [.. await client.GetDpListAsync()];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch DP list during /login for chat {ChatId}", chatId);
            await sender.SendTextAsync(chatId, "❌ Couldn't reach MeroShare right now. Try /login again shortly.");
            return;
        }

        var dp = dpList.FirstOrDefault(d => string.Equals(d.Code, dpInput, StringComparison.OrdinalIgnoreCase))
            ?? dpList.FirstOrDefault(d => d.Name.Contains(dpInput, StringComparison.OrdinalIgnoreCase));
        if (dp is null)
        {
            await sender.SendTextAsync(chatId,
                $"❌ \"{dpInput}\" isn't a recognized DP. Run /login again and enter the exact DP code shown on MeroShare's login page.");
            return;
        }

        if (accounts.IsUsernameLinked(username, dp.Code))
        {
            await sender.SendTextAsync(chatId,
                $"❌ This MeroShare account ({username} · {dp.Code}) is already linked on this bot — the same account can't be linked twice. If this is a mistake, contact the admin.");
            return;
        }

        await sender.SendTextAsync(chatId, "🔐 Verifying your MeroShare credentials...");
        MeroShareSession session;
        try
        {
            session = await client.LoginAsync(new MeroShareCredentials(username, password, dp.Code));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Login validation failed for chat {ChatId}", chatId);
            await sender.SendTextAsync(chatId,
                "❌ Login failed — check your username, DP and password, then run /login again.");
            return;
        }

        try { await client.LogoutAsync(session); } catch { /* best-effort */ }

        var index = accounts.AddAccount(chatId, username, dp.Code, password, crn, pin);
        if (index is null)
        {
            await sender.SendTextAsync(chatId,
                $"❌ This MeroShare account ({username} · {dp.Code}) is already linked on this bot — the same account can't be linked twice. If this is a mistake, contact the admin.");
            return;
        }

        var isOnlyAccount = accounts.GetAccounts(chatId).Count == 1;
        await sender.SendTextAsync(chatId,
            $"✅ Account #{index} ({username} · {dp.Code}) linked successfully." +
            (isOnlyAccount ? " It's set as your default account." : " Use /switch to change your default account.") +
            "\n\nUse /accounts to view all linked accounts.");
    }
}
