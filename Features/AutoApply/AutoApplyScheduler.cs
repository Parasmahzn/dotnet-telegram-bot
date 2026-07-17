using MeroShareBot.Features.Ipo;
using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.AutoApply;

// Called from IpoCheckerJob after the notify fan-out. Only ever SENDS a confirm-tap message —
// never calls the real apply API itself. The only code path that reaches ApplyIpoHandler for an
// autoapply-originated request is AutoApplyCallbackEndpoint, reachable exclusively from a real
// Telegram callback_query (a human tap). No timer/retry/background path submits unattended.
public sealed class AutoApplyScheduler(AccountStore store, TelegramSender sender)
{
    public async Task RunAsync(IReadOnlyList<IpoData> ipos, CancellationToken ct)
    {
        var eligible = ipos.Where(IsEligibleIpo.Check).ToList();
        if (eligible.Count == 0) return;

        foreach (var (chatId, account) in store.GetAutoApplyEnabledAccounts())
        {
            foreach (var ipo in eligible)
            {
                if (account.AppliedShareIds.Contains(ipo.CompanyShareId)) continue;
                if (account.AutoApplyPromptedShareIds.Contains(ipo.CompanyShareId)) continue;

                store.MarkAutoApplyPrompted(chatId, account.Id, ipo.CompanyShareId);

                var idHex = account.Id.ToString("N");
                var applyData = $"autoapply_go_{idHex}_{ipo.CompanyShareId}_{account.AutoApplyKitta}";
                var skipData = $"autoapply_skip_{idHex}_{ipo.CompanyShareId}";

                var text = $"🤖 Autoapply match!\n\n🏢 {ipo.Name} ({ipo.Symbol})\n👤 Account: {account.DisplayLabel}\n📦 Kitta: {account.AutoApplyKitta}\n\nTap to submit — nothing is applied until you tap.";
                await sender.SendKeyboardAsync(chatId, text,
                [
                    [InlineKeyboardButton.WithCallbackData("✅ Apply now", applyData)],
                    [InlineKeyboardButton.WithCallbackData("❌ Skip", skipData)],
                ], ct);
            }
        }
    }
}
