using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Shared.Telegram;

// Thin wrappers over the Bot API — plain text, no parse_mode (Node-app parity).
public sealed class TelegramSender(ITelegramBotClient bot)
{
    public Task SendTextAsync(long chatId, string text, CancellationToken ct = default) =>
        bot.SendMessage(chatId, text, cancellationToken: ct);

    public Task SendKeyboardAsync(
        long chatId,
        string text,
        IEnumerable<IEnumerable<InlineKeyboardButton>> buttons,
        CancellationToken ct = default) =>
        bot.SendMessage(chatId, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);

    public Task AnswerCallbackAsync(string callbackQueryId, CancellationToken ct = default) =>
        bot.AnswerCallbackQuery(callbackQueryId, cancellationToken: ct);
}
