using MeroShareBot.Features.Ipo.GetOpenIpos;
using MeroShareBot.Shared.Config;
using MeroShareBot.Shared.Telegram;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.Ipo.ApplyIpo;

// Port of handleApply / handleApplyCallback / handleApplyFromSched / executeApply
// from src/bot/commands/ipo.js — the two-step account → IPO selection state machine.
public sealed class ApplyIpoEndpoint(
    GetOpenIposHandler getOpenIpos,
    ApplyIpoHandler applyIpo,
    PendingApplyStore store,
    TelegramSender sender,
    IOptions<MeroShareOptions> opts)
{
    private const string SelectAccountsText = "🚀 Apply for IPO\n\nSelect accounts to apply with:";

    public async Task HandleMessageAsync(Message msg, string arg)
    {
        var chatId = msg.Chat.Id;
        var users = opts.Value.Users;
        if (users.Count == 0)
        {
            await sender.SendTextAsync(chatId, "No MeroShare accounts configured.");
            return;
        }

        await sender.SendTextAsync(chatId, "🔍 Fetching open IPOs...");
        var allIpos = await getOpenIpos.HandleAsync(users[0]);
        var eligibleIpos = allIpos.Where(IsEligibleIpo.Check).ToList();

        if (eligibleIpos.Count == 0)
        {
            await sender.SendTextAsync(chatId, "😴 No eligible IPOs open right now. Use /ipo to see all open issues.");
            return;
        }

        // If arg provided, filter to matching IPOs
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

        store.Set(chatId, new PendingApply(ApplyStep.Accounts, users, ipos));
        await sender.SendKeyboardAsync(chatId, SelectAccountsText, AccountButtons(users));
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

        // Tapped an apply button from /ipo list or scheduler notification
        if (data.StartsWith("apply_sched_"))
        {
            await HandleApplyFromSchedAsync(chatId, data["apply_sched_".Length..]);
            return;
        }

        var pending = store.Get(chatId);
        if (pending is null) return;

        // Step 1: account selection
        if (pending.Step == ApplyStep.Accounts)
        {
            IReadOnlyList<MeroShareUser>? selectedUsers = null;
            if (data == "apply_acct_all")
            {
                selectedUsers = pending.Users;
            }
            else if (data.StartsWith("apply_acct_"))
            {
                if (!int.TryParse(data["apply_acct_".Length..], out var index)) return;
                if (index < 0 || index >= pending.Users.Count) return;
                selectedUsers = [pending.Users[index]];
            }
            if (selectedUsers is null) return;

            // Single eligible IPO — skip step 2, execute directly
            if (pending.Ipos.Count == 1)
            {
                store.Remove(chatId);
                await ExecuteApplyAsync(chatId, pending.Ipos, selectedUsers);
                return;
            }

            // Multiple IPOs — show IPO selection keyboard
            store.Set(chatId, pending with { Step = ApplyStep.Ipos, SelectedUsers = selectedUsers });

            var accountLabel = selectedUsers.Count == pending.Users.Count
                ? $"all {selectedUsers.Count} accounts"
                : string.Join(", ", selectedUsers.Select(u => u.Username));
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

        // Step 2: IPO selection
        if (pending.Step == ApplyStep.Ipos && pending.SelectedUsers is { } selected)
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
        var users = opts.Value.Users;
        if (users.Count == 0)
        {
            await sender.SendTextAsync(chatId, "No MeroShare accounts configured.");
            return;
        }

        await sender.SendTextAsync(chatId, "🔍 Fetching open IPOs...");
        var allIpos = await getOpenIpos.HandleAsync(users[0]);
        var ipos = allIpos.Where(IsEligibleIpo.Check).Where(ipo => ipo.Symbol == symbol).ToList();

        if (ipos.Count == 0)
        {
            await sender.SendTextAsync(chatId, "⚠️ This issue is not eligible for ASBA application (only ordinary shares qualify). Use /ipo to see what's available.");
            return;
        }

        store.Set(chatId, new PendingApply(ApplyStep.Accounts, users, ipos));
        await sender.SendKeyboardAsync(chatId, SelectAccountsText, AccountButtons(users));
    }

    private async Task ExecuteApplyAsync(long chatId, IReadOnlyList<IpoData> ipos, IReadOnlyList<MeroShareUser> users)
    {
        var kitta = opts.Value.DefaultApplyKitta;
        Func<string, Task> notify = text => sender.SendTextAsync(chatId, text);

        foreach (var ipo in ipos)
        {
            if (ipos.Count > 1)
                await sender.SendTextAsync(chatId, $"\n📋 Applying for {ipo.Name}...");

            var results = await applyIpo.ApplyAllAccountsAsync(ipo.Name, kitta, users, notify);

            var allOk = results.All(r => r.Success);
            var summary = string.Join('\n', results.Select(r =>
                r.Success ? $"✅ {r.Username}: Applied" : $"❌ {r.Username}: {r.Error}"));
            var header = allOk
                ? $"🎉 All applications submitted for \"{ipo.Name}\"!"
                : $"📊 Results for \"{ipo.Name}\":";
            await sender.SendTextAsync(chatId, $"{header}\n\n{summary}");
        }
    }

    private static IEnumerable<InlineKeyboardButton[]> AccountButtons(IReadOnlyList<MeroShareUser> users) =>
        users
            .Select((u, i) => new[] { InlineKeyboardButton.WithCallbackData($"👤 {u.Username}", $"apply_acct_{i}") })
            .Append([InlineKeyboardButton.WithCallbackData($"✅ All accounts ({users.Count})", "apply_acct_all")])
            .Append([InlineKeyboardButton.WithCallbackData("❌ Cancel", "apply_no")]);
}
