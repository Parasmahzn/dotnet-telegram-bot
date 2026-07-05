using MeroShareBot.Features.Ipo;
using MeroShareBot.Features.Ipo.GetOpenIpos;
using MeroShareBot.Shared.Config;
using MeroShareBot.Shared.Telegram;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.Scheduler;

public sealed class IpoCheckerJob(
    GetOpenIposHandler getOpenIpos,
    TelegramSender sender,
    IOptions<MeroShareOptions> meroShareOpts,
    IOptions<TelegramOptions> telegramOpts,
    ILogger<IpoCheckerJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        logger.LogInformation("[IPO Scheduler] Checking for open IPOs...");

        var ipos = await getOpenIpos.HandleAsync(meroShareOpts.Value.Users[0]);
        if (ipos.Count == 0)
        {
            logger.LogInformation("[IPO Scheduler] No open IPOs found.");
            return;
        }

        var text = FormatIpoList(ipos);
        var buttons = ipos.Select((ipo, i) => new[]
        {
            InlineKeyboardButton.WithCallbackData($"🚀 {i + 1}. Apply — {ipo.Name}", $"apply_sched_{ipo.Symbol}"),
        });

        foreach (var chatId in telegramOpts.Value.AllowedChatIds)
        {
            await sender.SendKeyboardAsync(chatId, text, buttons, ct);
        }

        logger.LogInformation("[IPO Scheduler] Sent alert for {Count} IPO(s).", ipos.Count);
    }

    private static string FormatIpoList(IReadOnlyList<IpoData> ipos)
    {
        var lines = new List<string>
        {
            $"🔔 {ipos.Count} IPO{(ipos.Count > 1 ? "s" : "")} open for application!",
            "",
        };
        for (var i = 0; i < ipos.Count; i++)
        {
            lines.Add($"🏢 {i + 1}. {ipos[i].Name}");
            lines.Add($"   👥 {ipos[i].SubGroup}");
            lines.Add($"   📌 {ipos[i].Type} · {ipos[i].ShareType}");
            lines.Add("");
        }
        return string.Join('\n', lines).TrimEnd();
    }
}
