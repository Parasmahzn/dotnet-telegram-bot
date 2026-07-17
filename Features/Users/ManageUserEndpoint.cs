using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.Users;

// Admin-only /manageuser — pick a registered chat, then toggle IsAdmin/IsApplyAllowed/IsBlocked.
// Stateless: every render re-reads UserStore fresh and the target chat id round-trips through
// callback_data, same "rendered fresh, edited in place" shape as Features/Settings/SettingsEndpoint.
public sealed class ManageUserEndpoint(UserStore userStore, TelegramSender sender)
{
    public async Task HandleAsync(Message msg, bool isAdmin)
    {
        var chatId = msg.Chat.Id;
        if (!isAdmin)
        {
            await sender.SendTextAsync(chatId, "🚫 Admin only.");
            return;
        }

        var users = userStore.GetAllUsers();
        if (users.Count == 0)
        {
            await sender.SendTextAsync(chatId, "👥 No registered chats yet.");
            return;
        }

        await sender.SendKeyboardAsync(chatId, "👤 Pick a chat to manage:", PickerButtons(users));
    }

    public async Task HandleCallbackAsync(CallbackQuery cb, bool isAdmin)
    {
        if (cb.Message is not { } cbMsg) return;
        var chatId = cbMsg.Chat.Id;

        if (!isAdmin)
        {
            await sender.AnswerCallbackAsync(cb.Id, "🚫 Admin only.", showAlert: true);
            return;
        }

        var data = cb.Data ?? "";

        if (data == "manageuser_back")
        {
            await sender.AnswerCallbackAsync(cb.Id);
            var users = userStore.GetAllUsers();
            await sender.EditKeyboardAsync(chatId, cbMsg.MessageId, "👤 Pick a chat to manage:", PickerButtons(users));
            return;
        }

        if (data.StartsWith("manageuser_pick_") && long.TryParse(data["manageuser_pick_".Length..], out var pickId))
        {
            await sender.AnswerCallbackAsync(cb.Id);
            await RenderCardAsync(chatId, cbMsg.MessageId, pickId);
            return;
        }

        if (data.StartsWith("manageuser_toggleadmin_") && long.TryParse(data["manageuser_toggleadmin_".Length..], out var adminId))
        {
            var user = userStore.GetUser(adminId);
            if (user is null) { await sender.AnswerCallbackAsync(cb.Id, "❌ Chat not found.", showAlert: true); return; }
            userStore.SetAdmin(adminId, !user.IsAdmin);
            await sender.AnswerCallbackAsync(cb.Id, !user.IsAdmin ? "✅ Admin granted." : "🚫 Admin removed.");
            await RenderCardAsync(chatId, cbMsg.MessageId, adminId);
            return;
        }

        if (data.StartsWith("manageuser_toggleapply_") && long.TryParse(data["manageuser_toggleapply_".Length..], out var applyId))
        {
            var user = userStore.GetUser(applyId);
            if (user is null) { await sender.AnswerCallbackAsync(cb.Id, "❌ Chat not found.", showAlert: true); return; }
            userStore.SetApplyAllowed(applyId, !user.IsApplyAllowed);
            await sender.AnswerCallbackAsync(cb.Id, !user.IsApplyAllowed ? "✅ Apply enabled." : "🚫 Apply disabled.");
            await RenderCardAsync(chatId, cbMsg.MessageId, applyId);
            return;
        }

        if (data.StartsWith("manageuser_toggleblock_") && long.TryParse(data["manageuser_toggleblock_".Length..], out var blockId))
        {
            var user = userStore.GetUser(blockId);
            if (user is null) { await sender.AnswerCallbackAsync(cb.Id, "❌ Chat not found.", showAlert: true); return; }
            userStore.SetBlocked(blockId, !user.IsBlocked);
            await sender.AnswerCallbackAsync(cb.Id, !user.IsBlocked ? "🚫 Chat blocked." : "✅ Chat unblocked.");
            await RenderCardAsync(chatId, cbMsg.MessageId, blockId);
            return;
        }

        await sender.AnswerCallbackAsync(cb.Id);
    }

    private async Task RenderCardAsync(long chatId, int messageId, long targetChatId)
    {
        var user = userStore.GetUser(targetChatId);
        if (user is null)
        {
            await sender.EditTextAsync(chatId, messageId, "❌ That chat is no longer registered.");
            return;
        }

        var name = string.IsNullOrWhiteSpace(user.LastName) ? user.FirstName : $"{user.FirstName} {user.LastName}";
        var handle = string.IsNullOrEmpty(user.Username) ? "" : $" (@{user.Username})";
        var text =
            $"👤 {name}{handle}\n" +
            $"Chat id: {user.ChatId}\n" +
            $"Registered: {user.RegisteredAt:yyyy-MM-dd}\n\n" +
            $"Admin: {(user.IsAdmin ? "✅ yes" : "— no")}\n" +
            $"Apply allowed: {(user.IsApplyAllowed ? "✅ yes" : "— no")}\n" +
            $"Blocked: {(user.IsBlocked ? "🚫 yes" : "— no")}";

        IEnumerable<InlineKeyboardButton[]> buttons =
        [
            [InlineKeyboardButton.WithCallbackData(user.IsAdmin ? "Remove Admin" : "Make Admin", $"manageuser_toggleadmin_{user.ChatId}")],
            [InlineKeyboardButton.WithCallbackData(user.IsApplyAllowed ? "Disable Apply" : "Enable Apply", $"manageuser_toggleapply_{user.ChatId}")],
            [InlineKeyboardButton.WithCallbackData(user.IsBlocked ? "Unblock" : "Block", $"manageuser_toggleblock_{user.ChatId}")],
            [InlineKeyboardButton.WithCallbackData("◀️ Back", "manageuser_back")],
        ];

        await sender.EditKeyboardAsync(chatId, messageId, text, buttons);
    }

    private static IEnumerable<InlineKeyboardButton[]> PickerButtons(IReadOnlyList<UserRecord> users) =>
        users.Select(u =>
        {
            var handle = string.IsNullOrEmpty(u.Username) ? "" : $" (@{u.Username})";
            return new[] { InlineKeyboardButton.WithCallbackData($"{u.FirstName}{handle}", $"manageuser_pick_{u.ChatId}") };
        });
}
