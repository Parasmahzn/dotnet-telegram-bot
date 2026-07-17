namespace MeroShareBot.Features.AutoApply;

// /autoapply [n] on <kitta> | /autoapply [n] off | bare /autoapply shows status.
// Never submits unattended — enabling this only makes the scheduler send a confirm-tap button
// per eligible IPO (see AutoApplyScheduler); the real apply call always requires a tap.
public sealed class AutoApplyEndpoint(AccountStore store, TelegramSender sender)
{
    public async Task HandleAsync(Message msg, string arg)
    {
        var chatId = msg.Chat.Id;
        var accounts = store.GetAccounts(chatId);
        if (accounts.Count == 0)
        {
            await sender.SendTextAsync(chatId, "No accounts linked. Use /login to link one.");
            return;
        }

        var tokens = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        int index;
        string[] rest;
        if (tokens.Length > 0 && int.TryParse(tokens[0], out index))
        {
            rest = tokens[1..];
        }
        else
        {
            var defaultAccount = store.GetDefault(chatId);
            if (defaultAccount is null)
            {
                await sender.SendTextAsync(chatId, "Multiple accounts linked and no default set — specify an index: /autoapply <n> on <kitta>. See /accounts.");
                return;
            }
            index = accounts.ToList().FindIndex(a => a.Id == defaultAccount.Id) + 1;
            rest = tokens;
        }

        var account = store.GetAccount(chatId, index);
        if (account is null)
        {
            await sender.SendTextAsync(chatId, "❌ Invalid account index. Run /accounts to see valid indices.");
            return;
        }

        if (rest.Length == 0)
        {
            await sender.SendTextAsync(chatId,
                $"🤖 Autoapply for #{index} ({account.DisplayLabel}): {(account.AutoApplyEnabled ? $"ON ({account.AutoApplyKitta} kitta)" : "off")}\n\n" +
                "Usage: /autoapply <n> on <kitta> · /autoapply <n> off\n" +
                "You'll always get a confirm button before anything is actually submitted.");
            return;
        }

        if (rest[0].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            store.SetAutoApply(chatId, index, enabled: false, kitta: null);
            await sender.SendTextAsync(chatId, $"🤖 Autoapply disabled for #{index} ({account.DisplayLabel}).");
            return;
        }

        if (rest[0].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            if (rest.Length < 2 || !int.TryParse(rest[1], out var kitta) || kitta <= 0)
            {
                await sender.SendTextAsync(chatId, "Usage: /autoapply <n> on <kitta> — specify how many kitta to autoapply for.");
                return;
            }

            store.SetAutoApply(chatId, index, enabled: true, kitta: kitta);
            await sender.SendTextAsync(chatId,
                $"🤖 Autoapply enabled for #{index} ({account.DisplayLabel}), {kitta} kitta.\n" +
                "You'll get a confirm button whenever a new eligible IPO opens — nothing is submitted until you tap it.");
            return;
        }

        await sender.SendTextAsync(chatId, "Usage: /autoapply <n> on <kitta> · /autoapply <n> off");
    }
}
