using System.Globalization;
using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.Profile;

public sealed class GetProfileEndpoint(
    GetProfileHandler handler,
    AccountStore store,
    AccountResolver resolver,
    TelegramSender sender)
{
    public async Task HandleMessageAsync(Message msg, string arg)
    {
        var chatId = msg.Chat.Id;
        var account = resolver.Resolve(chatId, arg);
        if (account is not null)
        {
            await FetchAndSendProfileAsync(chatId, account);
            return;
        }

        var accounts = store.GetAccounts(chatId);
        if (accounts.Count == 0)
        {
            await sender.SendTextAsync(chatId, "No accounts linked. Use /login to link one.");
            return;
        }

        var buttons = accounts.Select((a, i) => new[]
        {
            InlineKeyboardButton.WithCallbackData($"👤 {a.Username}", $"profile_acct_{i + 1}"),
        });
        await sender.SendKeyboardAsync(chatId, "Pick an account — which profile do you want to check?", buttons);
    }

    public async Task HandleCallbackAsync(CallbackQuery cb)
    {
        if (cb.Message is not { } cbMsg) return;
        var chatId = cbMsg.Chat.Id;
        await sender.AnswerCallbackAsync(cb.Id);

        var data = cb.Data ?? "";
        if (!data.StartsWith("profile_acct_") || !int.TryParse(data["profile_acct_".Length..], out var index)) return;

        var account = store.GetAccount(chatId, index);
        if (account is null) return;
        await FetchAndSendProfileAsync(chatId, account);
    }

    private async Task FetchAndSendProfileAsync(long chatId, LinkedAccount account)
    {
        var creds = store.Decrypt(account);
        var result = await handler.HandleAsync(creds.Credentials, text => sender.SendTextAsync(chatId, text));

        if (!result.Success || result.Personal is null)
        {
            await sender.SendTextAsync(chatId, result.Error ?? $"Could not fetch profile for {account.Username}.");
            return;
        }

        var personal = result.Personal;
        var lines = new List<string>
        {
            $"👤 {personal.Name}",
            $"🆔 {personal.Username ?? account.Username}",
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
