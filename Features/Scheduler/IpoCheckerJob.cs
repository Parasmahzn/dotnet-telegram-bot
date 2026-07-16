using MeroShareBot.Features.Ipo;
using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.Scheduler;

public sealed class IpoCheckerJob(
    GetOpenIposHandler getOpenIpos,
    AccountStore accountStore,
    AutoApplyScheduler autoApply,
    TelegramSender sender,
    ILogger<IpoCheckerJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        logger.LogInformation("[IPO Scheduler] Checking for open IPOs...");

        var anyAccount = accountStore.GetAnyAccountForIssueListing();
        if (anyAccount is null)
        {
            logger.LogWarning("[IPO Scheduler] No linked accounts anywhere — skipping.");
            return;
        }

        var ipos = await getOpenIpos.HandleAsync(accountStore.Decrypt(anyAccount.Value.Account).Credentials);
        logger.LogInformation("[IPO Scheduler] {Count} open IPO(s).", ipos.Count);

        if (ipos.Count > 0)
        {
            var text = FormatIpoList(ipos);
            var buttons = ipos.Select((ipo, i) => new[]
            {
                InlineKeyboardButton.WithCallbackData($"🚀 {i + 1}. Apply — {ipo.Name}", $"apply_sched_{ipo.Symbol}"),
            });

            foreach (var chatId in accountStore.GetNotifyEnabledChatIds())
            {
                await sender.SendKeyboardAsync(chatId, text, buttons, ct);
            }
        }

        await autoApply.RunAsync(ipos, ct);
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
