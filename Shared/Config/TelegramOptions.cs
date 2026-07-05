using System.ComponentModel.DataAnnotations;

namespace MeroShareBot.Shared.Config;

public sealed class TelegramOptions
{
    [Required] public string BotToken { get; init; } = "";

    // When set, the bot registers this URL with Telegram at startup.
    // Leave empty if the webhook is registered externally (Node-app parity).
    public string WebhookUrl { get; init; } = "";

    public List<long> AllowedChatIds { get; init; } = [];
}
