namespace MeroShareBot.Features.Users;

// Admin-only /users command — lists every chat that has ever messaged the bot.
public sealed class UsersListEndpoint(UserStore userStore, AccountStore accountStore, TelegramSender sender)
{
    public async Task HandleAsync(Message msg, bool isAdmin)
    {
        var chatId = msg.Chat.Id;
        if (!isAdmin)
        {
            await sender.SendTextAsync(chatId, "🚫 Admin only.");
            return;
        }

        var chatIds = userStore.GetAllChatIds();
        if (chatIds.Count == 0)
        {
            await sender.SendTextAsync(chatId, "👥 No registered chats yet.");
            return;
        }

        var lines = chatIds.Select(id =>
        {
            var u = userStore.GetUser(id)!;
            var accountCount = accountStore.GetAccounts(id).Count;
            var name = string.IsNullOrWhiteSpace(u.LastName) ? u.FirstName : $"{u.FirstName} {u.LastName}";
            var handle = string.IsNullOrEmpty(u.Username) ? "" : $" (@{u.Username})";
            var admin = u.IsAdmin ? " · admin" : "";
            return $"{u.ChatId} — {name}{handle} — {accountCount} linked account(s), since {u.RegisteredAt:yyyy-MM-dd}{admin}";
        });

        await sender.SendTextAsync(chatId, "👥 Registered chats:\n\n" + string.Join('\n', lines));
    }
}
