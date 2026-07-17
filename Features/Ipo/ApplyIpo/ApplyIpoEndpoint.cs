using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.Ipo.ApplyIpo;

// The two-step account -> IPO selection state machine, now sourced from this chat's own linked
// accounts instead of a global config list.
public sealed class ApplyIpoEndpoint(
    GetOpenIposHandler getOpenIpos,
    ApplyIpoHandler applyIpo,
    PendingApplyStore store,
    AccountStore accountStore,
    IOptions<MeroShareOptions> opts,
    TelegramSender sender)
{
    private const string SelectAccountsText = "🚀 Apply for IPO\n\nSelect accounts to apply with:";

    public async Task HandleMessageAsync(Message msg, string arg)
    {
        var chatId = msg.Chat.Id;
        var accounts = accountStore.GetAccounts(chatId);
        if (accounts.Count == 0)
        {
            await sender.SendTextAsync(chatId, "No accounts linked. Use /login to link one.");
            return;
        }

        await sender.SendTextAsync(chatId, "🔍 Fetching open IPOs...");
        var allIpos = await getOpenIpos.HandleAsync(accountStore.Decrypt(accounts[0]).Credentials);
        var eligibleIpos = allIpos.Where(IsEligibleIpo.Check).ToList();

        if (eligibleIpos.Count == 0)
        {
            await sender.SendTextAsync(chatId, "😴 No eligible IPOs open right now. Use /ipo to see all open issues.");
            return;
        }

        IReadOnlyList<IpoData> ipos = eligibleIpos;
        if (!string.IsNullOrEmpty(arg))
        {
            ipos = eligibleIpos.Where(ipo =>
                ipo.Name.Contains(arg, StringComparison.OrdinalIgnoreCase) ||
                ipo.Symbol.Contains(arg, StringComparison.OrdinalIgnoreCase)).ToList();
            if (ipos.Count == 0)
            {
                await sender.SendTextAsync(chatId, $"❌ No eligible IPO found matching \"{arg}\".\n\nUse /ipo to see what's currently available.");
                return;
            }
        }

        store.Set(chatId, new PendingApply(ApplyStep.Accounts, accounts, ipos));
        await sender.SendKeyboardAsync(chatId, SelectAccountsText, AccountButtons(accounts));
    }

    public async Task HandleCallbackAsync(CallbackQuery cb)
    {
        if (cb.Message is not { } cbMsg) return;
        var chatId = cbMsg.Chat.Id;
        var data = cb.Data ?? "";

        await sender.AnswerCallbackAsync(cb.Id);

        if (data == "apply_no")
        {
            store.Remove(chatId);
            await sender.SendTextAsync(chatId, "❌ Application cancelled.");
            return;
        }

        if (data.StartsWith("apply_sched_"))
        {
            await HandleApplyFromSchedAsync(chatId, data["apply_sched_".Length..]);
            return;
        }

        var pending = store.Get(chatId);
        if (pending is null) return;

        if (pending.Step == ApplyStep.Accounts)
        {
            IReadOnlyList<LinkedAccount>? selectedAccounts = null;
            if (data == "apply_acct_all")
            {
                selectedAccounts = pending.Accounts;
            }
            else if (data.StartsWith("apply_acct_"))
            {
                if (!int.TryParse(data["apply_acct_".Length..], out var index)) return;
                if (index < 0 || index >= pending.Accounts.Count) return;
                selectedAccounts = [pending.Accounts[index]];
            }
            if (selectedAccounts is null) return;

            if (pending.Ipos.Count == 1)
            {
                store.Remove(chatId);
                await ExecuteApplyAsync(chatId, pending.Ipos, selectedAccounts);
                return;
            }

            store.Set(chatId, pending with { Step = ApplyStep.Ipos, SelectedAccounts = selectedAccounts });

            var accountLabel = selectedAccounts.Count == pending.Accounts.Count
                ? $"all {selectedAccounts.Count} accounts"
                : string.Join(", ", selectedAccounts.Select(a => a.DisplayLabel));
            await sender.SendTextAsync(chatId, $"👥 Accounts: {accountLabel}");

            var buttons = pending.Ipos
                .Select((ipo, i) => new[]
                {
                    InlineKeyboardButton.WithCallbackData($"🏢 {ipo.Name} ({ipo.Symbol})", $"apply_ipo_{i}"),
                })
                .Append([InlineKeyboardButton.WithCallbackData($"📋 All IPOs ({pending.Ipos.Count})", "apply_ipo_all")])
                .Append([InlineKeyboardButton.WithCallbackData("❌ Cancel", "apply_no")]);
            await sender.SendKeyboardAsync(chatId, "Which IPO(s) to apply for?", buttons);
            return;
        }

        if (pending.Step == ApplyStep.Ipos && pending.SelectedAccounts is { } selected)
        {
            IReadOnlyList<IpoData>? selectedIpos = null;
            if (data == "apply_ipo_all")
            {
                selectedIpos = pending.Ipos;
            }
            else if (data.StartsWith("apply_ipo_"))
            {
                if (!int.TryParse(data["apply_ipo_".Length..], out var index)) return;
                if (index < 0 || index >= pending.Ipos.Count) return;
                selectedIpos = [pending.Ipos[index]];
            }
            if (selectedIpos is null) return;

            store.Remove(chatId);
            await ExecuteApplyAsync(chatId, selectedIpos, selected);
        }
    }

    private async Task HandleApplyFromSchedAsync(long chatId, string symbol)
    {
        var accounts = accountStore.GetAccounts(chatId);
        if (accounts.Count == 0)
        {
            await sender.SendTextAsync(chatId, "No accounts linked. Use /login to link one.");
            return;
        }

        await sender.SendTextAsync(chatId, "🔍 Fetching open IPOs...");
        var allIpos = await getOpenIpos.HandleAsync(accountStore.Decrypt(accounts[0]).Credentials);
        var ipos = allIpos.Where(IsEligibleIpo.Check).Where(ipo => ipo.Symbol == symbol).ToList();

        if (ipos.Count == 0)
        {
            await sender.SendTextAsync(chatId, "⚠️ This issue is not eligible for ASBA application (only ordinary shares qualify). Use /ipo to see what's available.");
            return;
        }

        store.Set(chatId, new PendingApply(ApplyStep.Accounts, accounts, ipos));
        await sender.SendKeyboardAsync(chatId, SelectAccountsText, AccountButtons(accounts));
    }

    private async Task ExecuteApplyAsync(long chatId, IReadOnlyList<IpoData> ipos, IReadOnlyList<LinkedAccount> accounts)
    {
        var kitta = opts.Value.DefaultApplyKitta;
        Func<string, Task> notify = text => sender.SendTextAsync(chatId, text);

        foreach (var ipo in ipos)
        {
            if (ipos.Count > 1)
                await sender.SendTextAsync(chatId, $"\n📋 Applying for {ipo.Name}...");

            var results = new List<AccountApplyResult>();
            foreach (var account in accounts)
            {
                if (account.AppliedShareIds.Contains(ipo.CompanyShareId))
                {
                    results.Add(new AccountApplyResult(account.DisplayLabel, true, Message: "Already applied — skipped"));
                    continue;
                }

                await notify($"⚙️ Applying with account {account.DisplayLabel}...");
                var decrypted = accountStore.Decrypt(account);
                var result = await applyIpo.ApplyAsync(decrypted.Credentials, decrypted.ApplyCredentials, ipo, kitta);
                if (result.Success) accountStore.MarkApplied(chatId, account.Id, ipo.CompanyShareId);

                results.Add(new AccountApplyResult(account.DisplayLabel, result.Success, result.Message, result.Error));
                await notify(result.Success
                    ? $"✅ Applied successfully with {account.DisplayLabel}"
                    : $"❌ Failed for {account.DisplayLabel}: {result.Error}");
            }

            var allOk = results.All(r => r.Success);
            var summary = string.Join('\n', results.Select(r =>
                r.Success ? $"✅ {r.Username}: {r.Message ?? "Applied"}" : $"❌ {r.Username}: {r.Error}"));
            var header = allOk
                ? $"🎉 All applications submitted for \"{ipo.Name}\"!"
                : $"📊 Results for \"{ipo.Name}\":";
            await sender.SendTextAsync(chatId, $"{header}\n\n{summary}");
        }
    }

    private static IEnumerable<InlineKeyboardButton[]> AccountButtons(IReadOnlyList<LinkedAccount> accounts) =>
        accounts
            .Select((a, i) => new[] { InlineKeyboardButton.WithCallbackData($"👤 {a.DisplayLabel} ({a.Username})", $"apply_acct_{i}") })
            .Append([InlineKeyboardButton.WithCallbackData($"✅ All accounts ({accounts.Count})", "apply_acct_all")])
            .Append([InlineKeyboardButton.WithCallbackData("❌ Cancel", "apply_no")]);
}
