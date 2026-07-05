using MeroShareBot.Shared.Config;
using MeroShareBot.Shared.Telegram;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.Ipo.GetOpenIpos;

public sealed class GetOpenIposEndpoint(
    GetOpenIposHandler handler,
    TelegramSender sender,
    IOptions<MeroShareOptions> opts)
{
    public async Task HandleAsync(Message msg)
    {
        var chatId = msg.Chat.Id;
        var user = opts.Value.Users.FirstOrDefault();
        if (user is null)
        {
            await sender.SendTextAsync(chatId, "No MeroShare accounts configured.");
            return;
        }

        await sender.SendTextAsync(chatId, "🔍 Checking open IPOs...");
        var ipos = await handler.HandleAsync(user);

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
