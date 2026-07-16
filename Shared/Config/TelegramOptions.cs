using System.ComponentModel.DataAnnotations;

namespace MeroShareBot.Shared.Config;

public sealed class TelegramOptions
{
    [Required] public string BotToken { get; init; } = "";

    // Telegram Bot API base — override for a self-hosted Bot API server/proxy if ever needed.
    [Required] public string ApiUrl { get; init; } = "https://api.telegram.org/bot";
}
