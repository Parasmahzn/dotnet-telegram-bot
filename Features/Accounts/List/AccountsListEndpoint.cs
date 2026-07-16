namespace MeroShareBot.Features.Accounts.List;

public sealed class AccountsListEndpoint(AccountStore store, TelegramSender sender)
{
    public async Task HandleAsync(Message msg)
    {
        var chatId = msg.Chat.Id;
        var accounts = store.GetAccounts(chatId);
        if (accounts.Count == 0)
        {
            await sender.SendTextAsync(chatId, "No accounts linked yet. Use /login to link one.");
            return;
        }

        var defaultAccount = store.GetDefault(chatId);
        var lines = new List<string> { $"👤 Linked accounts ({accounts.Count}):", "" };
        for (var i = 0; i < accounts.Count; i++)
        {
            var a = accounts[i];
            var isDefault = defaultAccount?.Id == a.Id;
            lines.Add($"{i + 1}. {a.Username} · DP {a.Dp}{(isDefault ? "  ⭐ default" : "")}");
            lines.Add($"   🤖 Autoapply: {(a.AutoApplyEnabled ? $"ON ({a.AutoApplyKitta ?? 0} kitta)" : "off")}");
        }
        lines.Add("");
        lines.Add($"🔔 Notifications for this chat: {(store.GetChatNotify(chatId) ? "ON" : "off")} (see /notify)");
        lines.Add("Use /switch <n> to set default · /removeaccount <n> to unlink.");
        await sender.SendTextAsync(chatId, string.Join('\n', lines));
    }
}
