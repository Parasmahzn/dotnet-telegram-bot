using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Features.Broadcast;

// Admin-only /broadcast: checkbox multi-select over every registered chat, then a free-text
// message body, then a preview + confirm before sending. Cancel works from any step.
public sealed class BroadcastEndpoint(UserStore userStore, TelegramSender sender, BroadcastState store)
{
    private const int PreviewLength = 300;

    public async Task HandleAsync(Message msg, bool isAdmin)
    {
        var chatId = msg.Chat.Id;
        if (!isAdmin)
        {
            await sender.SendTextAsync(chatId, "🚫 Admin only.");
            return;
        }

        var candidates = userStore.GetAllChatIds().Select(id => userStore.GetUser(id)!).ToList();
        if (candidates.Count == 0)
        {
            await sender.SendTextAsync(chatId, "👥 No registered chats yet.");
            return;
        }

        var pending = new PendingBroadcast(BroadcastStep.Selecting, candidates, new HashSet<long>());
        store.Set(chatId, pending);
        await sender.SendKeyboardAsync(chatId, SelectionText(pending), SelectionButtons(pending));
    }

    public async Task HandleCallbackAsync(CallbackQuery cb)
    {
        if (cb.Message is not { } cbMsg) return;
        var chatId = cbMsg.Chat.Id;
        var data = cb.Data ?? "";
        await sender.AnswerCallbackAsync(cb.Id);

        if (data == "broadcast_cancel")
        {
            store.Remove(chatId);
            await sender.EditTextAsync(chatId, cbMsg.MessageId, "❌ Broadcast cancelled.");
            return;
        }

        var pending = store.Get(chatId);
        if (pending is null) return;

        if (pending.Step == BroadcastStep.Selecting)
        {
            if (data == "broadcast_all")
            {
                var allSelected = pending.Selected.Count == pending.Candidates.Count
                    ? new HashSet<long>()
                    : pending.Candidates.Select(c => c.ChatId).ToHashSet();
                pending = pending with { Selected = allSelected };
                store.Set(chatId, pending);
                await sender.EditKeyboardAsync(chatId, cbMsg.MessageId, SelectionText(pending), SelectionButtons(pending));
                return;
            }

            if (data.StartsWith("broadcast_toggle_") && int.TryParse(data["broadcast_toggle_".Length..], out var index)
                && index >= 0 && index < pending.Candidates.Count)
            {
                var targetId = pending.Candidates[index].ChatId;
                var selected = new HashSet<long>(pending.Selected);
                if (!selected.Remove(targetId)) selected.Add(targetId);
                pending = pending with { Selected = selected };
                store.Set(chatId, pending);
                await sender.EditKeyboardAsync(chatId, cbMsg.MessageId, SelectionText(pending), SelectionButtons(pending));
                return;
            }

            if (data == "broadcast_done")
            {
                if (pending.Selected.Count == 0)
                {
                    await sender.EditKeyboardAsync(chatId, cbMsg.MessageId,
                        SelectionText(pending) + "\n\n⚠️ Select at least one user first.", SelectionButtons(pending));
                    return;
                }

                store.Set(chatId, pending with { Step = BroadcastStep.AwaitingMessage });
                await sender.EditKeyboardAsync(chatId, cbMsg.MessageId,
                    $"📝 Type the message to broadcast to {pending.Selected.Count} user(s), or tap Cancel.",
                    [[InlineKeyboardButton.WithCallbackData("❌ Cancel", "broadcast_cancel")]]);
                return;
            }

            return;
        }

        if (pending.Step == BroadcastStep.Confirming && data == "broadcast_send")
        {
            var recipients = pending.Selected.ToList();
            var failed = new List<long>();
            foreach (var recipientId in recipients)
            {
                try
                {
                    await sender.SendTextAsync(recipientId, pending.MessageText!);
                }
                catch (Exception)
                {
                    failed.Add(recipientId);
                }
            }

            store.Remove(chatId);
            var summary = $"✅ Sent to {recipients.Count - failed.Count}/{recipients.Count} user(s).";
            if (failed.Count > 0) summary += $"\n⚠️ Failed: {string.Join(", ", failed)}";
            await sender.EditTextAsync(chatId, cbMsg.MessageId, summary);
        }
    }

    public async Task HandleMessageReplyAsync(Message msg)
    {
        var chatId = msg.Chat.Id;
        var pending = store.Get(chatId);
        if (pending is null || pending.Step != BroadcastStep.AwaitingMessage) return;

        var text = (msg.Text ?? "").Trim();
        if (text.Length == 0)
        {
            await sender.SendTextAsync(chatId, "Message can't be empty. Send some text, or tap Cancel on the message above.");
            return;
        }

        store.Set(chatId, pending with { Step = BroadcastStep.Confirming, MessageText = text });

        var names = string.Join(", ", pending.Candidates.Where(c => pending.Selected.Contains(c.ChatId)).Select(c => c.FirstName));
        var preview = text.Length > PreviewLength ? text[..PreviewLength] + "…" : text;
        await sender.SendKeyboardAsync(chatId,
            $"📢 Send to {pending.Selected.Count} user(s): {names}\n\n---\n{preview}\n---\n\nConfirm?",
            [
                [InlineKeyboardButton.WithCallbackData("✅ Send", "broadcast_send")],
                [InlineKeyboardButton.WithCallbackData("❌ Cancel", "broadcast_cancel")],
            ]);
    }

    private static string SelectionText(PendingBroadcast pending) =>
        $"📢 Broadcast\n\nSelect recipients ({pending.Selected.Count}/{pending.Candidates.Count} selected):";

    private static IEnumerable<InlineKeyboardButton[]> SelectionButtons(PendingBroadcast pending)
    {
        var rows = pending.Candidates.Select((c, i) => new[]
        {
            InlineKeyboardButton.WithCallbackData(
                $"{(pending.Selected.Contains(c.ChatId) ? "✅" : "⬜")} {c.FirstName}",
                $"broadcast_toggle_{i}"),
        });

        var allSelected = pending.Selected.Count == pending.Candidates.Count;
        return rows
            .Append([InlineKeyboardButton.WithCallbackData($"{(allSelected ? "☑️" : "⬜")} Select all ({pending.Candidates.Count})", "broadcast_all")])
            .Append([InlineKeyboardButton.WithCallbackData($"▶️ Done ({pending.Selected.Count} selected)", "broadcast_done")])
            .Append([InlineKeyboardButton.WithCallbackData("❌ Cancel", "broadcast_cancel")]);
    }
}
