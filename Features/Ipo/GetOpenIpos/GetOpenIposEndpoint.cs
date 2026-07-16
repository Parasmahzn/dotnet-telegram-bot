using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.Ipo.GetOpenIpos;

public sealed class GetOpenIposEndpoint(
    GetOpenIposHandler handler,
    AccountStore store,
    AccountResolver resolver,
    TelegramSender sender)
{
    public async Task HandleAsync(Message msg, string arg)
    {
        var chatId = msg.Chat.Id;
        // The issue list is account-agnostic — any linked account supplies a valid session.
        var account = resolver.Resolve(chatId, arg) ?? store.GetAccounts(chatId).FirstOrDefault();
        if (account is null)
        {
            await sender.SendTextAsync(chatId, "No accounts linked. Use /login to link one (any account can be used to browse IPOs).");
            return;
        }

        await sender.SendTextAsync(chatId, "🔍 Checking open IPOs...");
        var ipos = await handler.HandleAsync(store.Decrypt(account).Credentials);

        if (ipos.Count == 0)
        {
            await sender.SendTextAsync(chatId, "😴 No open IPOs right now. Check back later!");
            return;
        }

        var lines = new List<string> { $"📋 Open IPOs ({ipos.Count} found):", "" };
        for (var i = 0; i < ipos.Count; i++)
        {
            lines.Add($"🏢 {i + 1}. {ipos[i].Name}");
            lines.Add($"   👥 {ipos[i].SubGroup}");
            lines.Add($"   📌 {ipos[i].Type} · {ipos[i].ShareType}");
            lines.Add("");
        }
        var text = string.Join('\n', lines).TrimEnd();

        var buttons = ipos.Select((ipo, i) => new[]
        {
            InlineKeyboardButton.WithCallbackData($"🚀 {i + 1}. Apply — {ipo.Name}", $"apply_sched_{ipo.Symbol}"),
        });
        await sender.SendKeyboardAsync(chatId, text, buttons);
    }
}
