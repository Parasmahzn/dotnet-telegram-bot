using Telegram.Bot.Types.ReplyMarkups;

namespace MeroShareBot.Shared.Telegram;

// Thin wrappers over the Bot API — plain text, no parse_mode. Every outgoing
// message is logged (chat + truncated text) for basic action-audit visibility.
public sealed class TelegramSender(ITelegramBotClient bot, ILogger<TelegramSender> logger)
{
    public Task SendTextAsync(long chatId, string text, CancellationToken ct = default)
    {
        logger.LogInformation("[SendText] chatId={ChatId}: \"{Text}\"", chatId, Truncate(text));
        return bot.SendMessage(chatId, text, cancellationToken: ct);
    }

    public Task SendKeyboardAsync(
        long chatId,
        string text,
        IEnumerable<IEnumerable<InlineKeyboardButton>> buttons,
        CancellationToken ct = default)
    {
        logger.LogInformation("[SendKeyboard] chatId={ChatId}: \"{Text}\"", chatId, Truncate(text));
        return bot.SendMessage(chatId, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    public Task EditTextAsync(long chatId, int messageId, string text, CancellationToken ct = default)
    {
        logger.LogInformation("[EditText] chatId={ChatId} messageId={MessageId}: \"{Text}\"", chatId, messageId, Truncate(text));
        return bot.EditMessageText(chatId, messageId, text, cancellationToken: ct);
    }

    public Task EditKeyboardAsync(
        long chatId,
        int messageId,
        string text,
        IEnumerable<IEnumerable<InlineKeyboardButton>> buttons,
        CancellationToken ct = default)
    {
        logger.LogInformation("[EditKeyboard] chatId={ChatId} messageId={MessageId}: \"{Text}\"", chatId, messageId, Truncate(text));
        return bot.EditMessageText(chatId, messageId, text,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: ct);
    }

    public Task AnswerCallbackAsync(string callbackQueryId, CancellationToken ct = default) =>
        bot.AnswerCallbackQuery(callbackQueryId, cancellationToken: ct);

    private static string Truncate(string s) => s.Length > 200 ? s[..200] : s;
}
