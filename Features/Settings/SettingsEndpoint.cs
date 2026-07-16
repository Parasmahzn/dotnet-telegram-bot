using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.Settings;

// Inline settings hub — rendered fresh from AccountStore state each time, edited in place rather
// than sending a new message per interaction.
public sealed class SettingsEndpoint(AccountStore store, SettingsKittaPromptState kittaState, TelegramSender sender)
{
    public Task HandleCommandAsync(Message msg) => RenderAsync(msg.Chat.Id, messageId: null);

    public async Task HandleCallbackAsync(CallbackQuery cb)
    {
        if (cb.Message is not { } cbMsg) return;
        var chatId = cbMsg.Chat.Id;
        await sender.AnswerCallbackAsync(cb.Id);
        var data = cb.Data ?? "";

        if (data == "settings_close")
        {
            await sender.EditTextAsync(chatId, cbMsg.MessageId, "Settings closed.");
            return;
        }

        if (data == "settings_notify_toggle")
        {
            store.SetChatNotify(chatId, !store.GetChatNotify(chatId));
        }
        else if (data.StartsWith("settings_default_") && int.TryParse(data["settings_default_".Length..], out var di))
        {
            store.SetDefault(chatId, di);
        }
        else if (data.StartsWith("settings_autoapply_toggle_") && int.TryParse(data["settings_autoapply_toggle_".Length..], out var ti))
        {
            var account = store.GetAccount(chatId, ti);
            if (account is not null)
            {
                if (!account.AutoApplyEnabled && account.AutoApplyKitta is null)
                {
                    kittaState.Await(chatId, ti);
                    await sender.SendTextAsync(chatId, $"How many kitta should account #{ti} autoapply for? (send a number)");
                    return;
                }
                store.SetAutoApply(chatId, ti, !account.AutoApplyEnabled, account.AutoApplyKitta);
            }
        }
        else if (data.StartsWith("settings_autoapply_kitta_") && int.TryParse(data["settings_autoapply_kitta_".Length..], out var ki))
        {
            kittaState.Await(chatId, ki);
            await sender.SendTextAsync(chatId, $"How many kitta should account #{ki} autoapply for? (send a number)");
            return;
        }

        await RenderAsync(chatId, cbMsg.MessageId);
    }

    public async Task HandleKittaReplyAsync(Message msg)
    {
        var chatId = msg.Chat.Id;
        var index = kittaState.Get(chatId);
        if (index is null) return;
        kittaState.Clear(chatId);

        if (!int.TryParse((msg.Text ?? "").Trim(), out var kitta) || kitta <= 0)
        {
            await sender.SendTextAsync(chatId, "That doesn't look like a valid kitta amount. Run /settings again.");
            return;
        }

        store.SetAutoApply(chatId, index.Value, enabled: true, kitta);
        await RenderAsync(chatId, messageId: null);
    }

    private async Task RenderAsync(long chatId, int? messageId)
    {
        var accounts = store.GetAccounts(chatId);
        if (accounts.Count == 0)
        {
            await sender.SendTextAsync(chatId, "No accounts linked yet. Use /login to link one, then /settings.");
            return;
        }

        var defaultAccount = store.GetDefault(chatId);
        var notify = store.GetChatNotify(chatId);

        var lines = new List<string> { "⚙️ Settings", "", $"🔔 Notifications: {(notify ? "ON" : "off")}" };
        var buttons = new List<InlineKeyboardButton[]>();
        buttons.Add([InlineKeyboardButton.WithCallbackData(notify ? "🔕 Turn notify off" : "🔔 Turn notify on", "settings_notify_toggle")]);

        for (var i = 0; i < accounts.Count; i++)
        {
            var a = accounts[i];
            var n = i + 1;
            var isDefault = defaultAccount?.Id == a.Id;
            lines.Add($"\n{n}. {a.Username}{(isDefault ? " ⭐" : "")} — autoapply {(a.AutoApplyEnabled ? $"ON ({a.AutoApplyKitta})" : "off")}");
            buttons.Add([
                InlineKeyboardButton.WithCallbackData(isDefault ? "⭐ Default" : "Set default", $"settings_default_{n}"),
                InlineKeyboardButton.WithCallbackData(a.AutoApplyEnabled ? "Autoapply off" : "Autoapply on", $"settings_autoapply_toggle_{n}"),
                InlineKeyboardButton.WithCallbackData("Set kitta", $"settings_autoapply_kitta_{n}"),
            ]);
        }
        buttons.Add([InlineKeyboardButton.WithCallbackData("Close", "settings_close")]);

        var text = string.Join('\n', lines);
        if (messageId is null) await sender.SendKeyboardAsync(chatId, text, buttons);
        else await sender.EditKeyboardAsync(chatId, messageId.Value, text, buttons);
    }
}
