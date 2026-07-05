using System.Globalization;
using MeroShareBot.Shared.Config;
using MeroShareBot.Shared.Telegram;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.Profile;

// Port of handleProfile / handleProfileCallback from src/bot/commands/profile.js.
public sealed class GetProfileEndpoint(
    GetProfileHandler handler,
    TelegramSender sender,
    IOptions<MeroShareOptions> opts)
{
    public async Task HandleMessageAsync(Message msg, string arg)
    {
        var chatId = msg.Chat.Id;
        var users = opts.Value.Users;
        var user = UserResolver.Resolve(users, arg);

        if (user is null)
        {
            if (users.Count == 1)
            {
                await FetchAndSendProfileAsync(chatId, users[0]);
                return;
            }
            var buttons = users
                .Select((u, i) => new[]
                {
                    InlineKeyboardButton.WithCallbackData($"👤 {u.Username}", $"profile_user_{i}"),
                })
                .Append([InlineKeyboardButton.WithCallbackData($"👥 All accounts ({users.Count})", "profile_all")]);
            await sender.SendKeyboardAsync(chatId, "Pick an account — which profile do you want to check?", buttons);
            return;
        }

        await FetchAndSendProfileAsync(chatId, user);
    }

    public async Task HandleCallbackAsync(CallbackQuery cb)
    {
        if (cb.Message is not { } cbMsg) return;
        var chatId = cbMsg.Chat.Id;

        await sender.AnswerCallbackAsync(cb.Id);
        var data = cb.Data ?? "";
        var users = opts.Value.Users;

        if (data == "profile_all")
        {
            foreach (var user in users)
            {
                await FetchAndSendProfileAsync(chatId, user);
            }
            return;
        }

        if (!int.TryParse(data.Replace("profile_user_", ""), out var index)) return;
        if (index < 0 || index >= users.Count) return;
        await FetchAndSendProfileAsync(chatId, users[index]);
    }

    private async Task FetchAndSendProfileAsync(long chatId, MeroShareUser user)
    {
        var result = await handler.HandleAsync(user, text => sender.SendTextAsync(chatId, text));

        if (!result.Success || result.Personal is null)
        {
            await sender.SendTextAsync(chatId, result.Error ?? $"Could not fetch profile for {user.Username}.");
            return;
        }

        var personal = result.Personal;
        var lines = new List<string>
        {
            $"👤 {personal.Name}",
            $"🆔 {personal.Username ?? user.Username}",
            "",
            "━━ Personal Information ━━",
            $"📋 BOID:    {personal.Boid}",
            $"🚻 Gender:  {personal.Gender}",
            $"📧 Email:   {personal.Email}",
            $"📞 Phone:   {personal.Phone}",
            "",
            "━━ Account Information ━━",
        };

        foreach (var entry in result.Account ?? [])
        {
            var isExpiry = entry.Label.Contains("expir", StringComparison.OrdinalIgnoreCase);
            var (prefix, note) = isExpiry ? DateStatus(entry.Value) : ("🗓️", "");
            lines.Add($"{prefix} {entry.Label}: {entry.Value}{(note.Length > 0 ? " " + note : "")}");
        }

        await sender.SendTextAsync(chatId, string.Join('\n', lines));
    }

    private static (string Prefix, string Note) DateStatus(string value)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return ("🗓️", "");

        var daysLeft = (int)Math.Ceiling((date - DateTime.Now).TotalDays);
        if (daysLeft < 0) return ("🔴", $"(expired {Math.Abs(daysLeft)} days ago)");
        if (daysLeft <= 15) return ("🔴", $"({daysLeft} days left)");
        if (daysLeft <= 30) return ("🟡", $"({daysLeft} days left)");
        return ("🗓️", "");
    }
}
