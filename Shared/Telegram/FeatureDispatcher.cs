namespace MeroShareBot.Shared.Telegram;

// Single router — command/callback routing. The bot is open to any chat; admin status (gating
// only the /users command) lives on UserRecord.IsAdmin in data/users.json, not a config allow-list.
public sealed class FeatureDispatcher(
    IServiceProvider sp,
    TelegramSender sender,
    UserStore userStore)
{
    private const string BlockedMessage = "🚫 You're no longer allowed to use this bot.";
    private const string ApplyDisabledMessage = "🚫 Applying is currently disabled for your account. Contact the admin if you think this is a mistake.";

    public async Task DispatchAsync(Update update)
    {
        RegisterSender(update);

        // Callbacks: route by callback_data prefix
        if (update.CallbackQuery is { } cb)
        {
            var cbChatId = cb.Message?.Chat.Id;
            if (cbChatId is not null && userStore.IsBlocked(cbChatId.Value))
            {
                await sender.AnswerCallbackAsync(cb.Id);
                await sender.SendTextAsync(cbChatId.Value, BlockedMessage);
                return;
            }

            var data = cb.Data ?? "";
            if (data.StartsWith("apply_"))
            {
                if (cbChatId is not null && !userStore.IsApplyAllowed(cbChatId.Value))
                {
                    await sender.AnswerCallbackAsync(cb.Id);
                    await sender.SendTextAsync(cbChatId.Value, ApplyDisabledMessage);
                    return;
                }
                await sp.GetRequiredService<ApplyIpoEndpoint>().HandleCallbackAsync(cb);
            }
            else if (data.StartsWith("profile_"))
                await sp.GetRequiredService<GetProfileEndpoint>().HandleCallbackAsync(cb);
            else if (data.StartsWith("portfolio_"))
                await sp.GetRequiredService<GetPortfolioEndpoint>().HandleCallbackAsync(cb);
            else if (data.StartsWith("autoapply_"))
            {
                if (cbChatId is not null && !userStore.IsApplyAllowed(cbChatId.Value))
                {
                    await sender.AnswerCallbackAsync(cb.Id);
                    await sender.SendTextAsync(cbChatId.Value, ApplyDisabledMessage);
                    return;
                }
                await sp.GetRequiredService<AutoApplyCallbackEndpoint>().HandleCallbackAsync(cb);
            }
            else if (data.StartsWith("settings_"))
                await sp.GetRequiredService<SettingsEndpoint>().HandleCallbackAsync(cb);
            else if (data.StartsWith("broadcast_"))
                await sp.GetRequiredService<BroadcastEndpoint>().HandleCallbackAsync(cb);
            else if (data.StartsWith("start_") && cb.Message is { } startMsg)
            {
                await sender.AnswerCallbackAsync(cb.Id);
                switch (data)
                {
                    case "start_login":
                        await sp.GetRequiredService<LoginEndpoint>().HandleAsync(startMsg);
                        break;
                    case "start_portfolio":
                        await sp.GetRequiredService<GetPortfolioEndpoint>().HandleMessageAsync(startMsg, "");
                        break;
                    case "start_apply":
                        if (cbChatId is not null && !userStore.IsApplyAllowed(cbChatId.Value))
                        {
                            await sender.SendTextAsync(cbChatId.Value, ApplyDisabledMessage);
                            break;
                        }
                        await sp.GetRequiredService<ApplyIpoEndpoint>().HandleMessageAsync(startMsg, "");
                        break;
                }
            }
            return;
        }

        if (update.Message is not { } msg) return;
        var chatId = msg.Chat.Id;

        if (userStore.IsBlocked(chatId))
        {
            await sender.SendTextAsync(chatId, BlockedMessage);
            return;
        }

        var text = msg.Text ?? "";

        if (!text.StartsWith('/'))
        {
            // Free-text priority: an in-progress wizard/prompt wins over the fallback message.
            var loginState = sp.GetRequiredService<LoginWizardState>();
            if (loginState.HasPending(chatId))
            {
                await sp.GetRequiredService<LoginEndpoint>().HandleFreeTextAsync(msg);
                return;
            }

            var kittaState = sp.GetRequiredService<SettingsKittaPromptState>();
            if (kittaState.Get(chatId) is not null)
            {
                await sp.GetRequiredService<SettingsEndpoint>().HandleKittaReplyAsync(msg);
                return;
            }

            var broadcastState = sp.GetRequiredService<BroadcastState>();
            if (broadcastState.Get(chatId) is { Step: BroadcastStep.AwaitingMessage })
            {
                await sp.GetRequiredService<BroadcastEndpoint>().HandleMessageReplyAsync(msg);
                return;
            }

            await sender.SendTextAsync(chatId, FallbackMessages.Random());
            return;
        }

        var parts = text.Split(' ', 2);
        var cmd = parts[0][1..].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1].Trim() : "";

        switch (cmd)
        {
            case "start":
                await sp.GetRequiredService<HelpEndpoint>().HandleStartAsync(msg);
                break;
            case "help":
                await sp.GetRequiredService<HelpEndpoint>().HandleHelpAsync(msg);
                break;
            case "login":
                await sp.GetRequiredService<LoginEndpoint>().HandleAsync(msg);
                break;
            case "accounts":
                await sp.GetRequiredService<AccountsListEndpoint>().HandleAsync(msg);
                break;
            case "switch":
                await sp.GetRequiredService<SwitchAccountEndpoint>().HandleAsync(msg, arg);
                break;
            case "removeaccount":
                await sp.GetRequiredService<RemoveAccountEndpoint>().HandleAsync(msg, arg);
                break;
            case "profile":
                await sp.GetRequiredService<GetProfileEndpoint>().HandleMessageAsync(msg, arg);
                break;
            case "portfolio":
                await sp.GetRequiredService<GetPortfolioEndpoint>().HandleMessageAsync(msg, arg);
                break;
            case "ipo":
                await sp.GetRequiredService<GetOpenIposEndpoint>().HandleAsync(msg, arg);
                break;
            case "apply":
                if (!userStore.IsApplyAllowed(chatId)) { await sender.SendTextAsync(chatId, ApplyDisabledMessage); break; }
                await sp.GetRequiredService<ApplyIpoEndpoint>().HandleMessageAsync(msg, arg);
                break;
            case "market":
                await sp.GetRequiredService<MarketEndpoint>().HandleAsync(msg, arg);
                break;
            case "watch":
                await sp.GetRequiredService<WatchlistEndpoint>().HandleAsync(msg, arg);
                break;
            case "notify":
                await sp.GetRequiredService<NotifyEndpoint>().HandleAsync(msg, arg);
                break;
            case "autoapply":
                if (!userStore.IsApplyAllowed(chatId)) { await sender.SendTextAsync(chatId, ApplyDisabledMessage); break; }
                await sp.GetRequiredService<AutoApplyEndpoint>().HandleAsync(msg, arg);
                break;
            case "settings":
                await sp.GetRequiredService<SettingsEndpoint>().HandleCommandAsync(msg);
                break;
            case "users":
                await sp.GetRequiredService<UsersListEndpoint>().HandleAsync(msg, userStore.IsAdmin(chatId));
                break;
            case "broadcast":
                await sp.GetRequiredService<BroadcastEndpoint>().HandleAsync(msg, userStore.IsAdmin(chatId));
                break;
            default:
                await sender.SendTextAsync(chatId, FallbackMessages.Random());
                break;
        }
    }

    private void RegisterSender(Update update)
    {
        var from = update.Message?.From ?? update.CallbackQuery?.From;
        var chatId = update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id;
        if (from is null || chatId is null) return;

        userStore.RegisterUser(chatId.Value, from.FirstName, from.LastName ?? "", from.Username ?? "");
    }
}
