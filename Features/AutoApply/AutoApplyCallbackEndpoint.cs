using MeroShareBot.Features.Ipo;

namespace MeroShareBot.Features.AutoApply;

// The ONLY place an autoapply-originated request reaches the real apply API — only ever invoked
// from a genuine Telegram callback_query (a human tapping the button AutoApplyScheduler sent).
public sealed class AutoApplyCallbackEndpoint(
    AccountStore store,
    MeroShareApiClient client,
    ApplyIpoHandler applyHandler,
    TelegramSender sender,
    ILogger<AutoApplyCallbackEndpoint> logger)
{
    public async Task HandleCallbackAsync(CallbackQuery cb)
    {
        if (cb.Message is not { } cbMsg) return;
        var chatId = cbMsg.Chat.Id;
        await sender.AnswerCallbackAsync(cb.Id);

        var parts = (cb.Data ?? "").Split('_'); // ["autoapply","go"|"skip", idHex, shareId, (kitta)]
        if (parts.Length < 4 || !Guid.TryParseExact(parts[2], "N", out var accountId) || !int.TryParse(parts[3], out var companyShareId))
            return;

        var account = store.GetAccountById(chatId, accountId);
        if (account is null)
        {
            await sender.SendTextAsync(chatId, "That account no longer exists.");
            return;
        }

        if (parts[1] == "skip")
        {
            await sender.SendTextAsync(chatId, "Skipped — you won't be asked again for this issue.");
            return;
        }

        if (parts[1] != "go") return;

        if (!account.AutoApplyEnabled)
        {
            await sender.SendTextAsync(chatId, "Autoapply was turned off for this account since this prompt was sent.");
            return;
        }

        if (parts.Length < 5 || !int.TryParse(parts[4], out var kitta))
        {
            await sender.SendTextAsync(chatId, "❌ Couldn't read the kitta amount from this button — run /autoapply to check your settings.");
            return;
        }

        var decrypted = store.Decrypt(account);
        await sender.SendTextAsync(chatId, $"⚙️ Applying with {account.DisplayLabel}...");

        IpoData ipo;
        try
        {
            var detail = await client.GetIssueDetailAsync(decrypted.Credentials, companyShareId);
            ipo = new IpoData(detail.CompanyShareId, detail.CompanyName, detail.Scrip, "",
                detail.ShareTypeName ?? "", detail.ShareGroupName ?? "");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Autoapply: failed to look up issue {CompanyShareId} for {Username}", companyShareId, account.Username);
            await sender.SendTextAsync(chatId, "❌ Couldn't look up this issue — it may no longer be open.");
            return;
        }

        var result = await applyHandler.ApplyAsync(decrypted.Credentials, decrypted.ApplyCredentials, ipo, kitta);
        if (result.Success) store.MarkApplied(chatId, account.Id, companyShareId);

        await sender.SendTextAsync(chatId, result.Success
            ? $"✅ Applied for {ipo.Name} ({kitta} kitta) with {account.DisplayLabel}."
            : $"❌ Failed: {result.Error}");
    }
}
