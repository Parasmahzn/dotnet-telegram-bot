namespace MeroShareBot.Features.Accounts.Login;

public sealed class LoginEndpoint(
    LoginWizardState state,
    AccountStore accounts,
    MeroShareApiClient client,
    IMeroShareSessionCache sessions,
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
        var wizardSession = state.Get(chatId);
        if (wizardSession is null) return;

        var raw = (msg.Text ?? "").Trim();
        if (raw.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            state.Clear(chatId);
            await sender.SendTextAsync(chatId, "❌ Login cancelled.");
            return;
        }

        var step = wizardSession.Steps.Dequeue();
        wizardSession.Collected[step.Key] = step.Optional && raw.Equals("skip", StringComparison.OrdinalIgnoreCase) ? "" : raw;

        if (wizardSession.Steps.Count > 0)
        {
            await sender.SendTextAsync(chatId, wizardSession.Steps.Peek().Prompt);
            return;
        }

        state.Clear(chatId);
        if (wizardSession.AwaitingLabel)
            await CompleteAsync(chatId, wizardSession.Collected);
        else
            await FinishAsync(chatId, wizardSession.Collected);
    }

    private async Task FinishAsync(long chatId, Dictionary<string, string> c)
    {
        var username = c["Username"];
        var dpInput = c["Dp"];
        var password = c["Password"];

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
        try
        {
            await sessions.GetSessionAsync(new MeroShareCredentials(username, password, dp.Code));
        }
        catch (Exception ex)
        {
            var message = ex.Resolve(logger, "Login validation",
                "Login failed — check your username, DP and password, then run /login again.");
            await sender.SendTextAsync(chatId, $"❌ {message}");
            return;
        }

        // Validated — store the resolved DP code (not the raw user input) and move to the label
        // step. Only reached after a real successful login, so a failed attempt never sees this.
        var forLabel = new Dictionary<string, string>(c) { ["Dp"] = dp.Code };
        state.Start(chatId, new WizardSession
        {
            Collected = forLabel,
            Steps = new Queue<FieldPrompt>([LoginWizardState.LabelPrompt]),
            AwaitingLabel = true,
        });
        await sender.SendTextAsync(chatId, LoginWizardState.LabelPrompt.Prompt);
    }

    private async Task CompleteAsync(long chatId, Dictionary<string, string> c)
    {
        var username = c["Username"];
        var dpCode = c["Dp"];
        var password = c["Password"];
        var crn = c.GetValueOrDefault("Crn", "");
        var pin = c.GetValueOrDefault("Pin", "");
        var label = c.GetValueOrDefault("Label", "");

        var index = accounts.AddAccount(chatId, username, dpCode, password, crn, pin, label);
        if (index is null)
        {
            await sender.SendTextAsync(chatId,
                $"❌ This MeroShare account ({username} · {dpCode}) is already linked on this bot — the same account can't be linked twice. If this is a mistake, contact the admin.");
            return;
        }

        var account = accounts.GetAccount(chatId, index.Value)!;
        var isOnlyAccount = accounts.GetAccounts(chatId).Count == 1;
        await sender.SendTextAsync(chatId,
            $"✅ Account added successfully!\n\n🏷️ Label: {account.Label}\n👤 Username: {username}\n🏦 DP: {dpCode}\n\n" +
            "Your account is now ready for IPO applications." +
            (isOnlyAccount ? " It's set as your default account." : " Use /switch to change your default account.") +
            "\n\nUse /accounts to view all linked accounts.");
    }
}
