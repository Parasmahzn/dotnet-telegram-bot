using MeroShareBot.Shared.Config;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace MeroShareBot.Shared.Telegram;

// Registers the webhook with Telegram at startup — only when Telegram:WebhookUrl is set.
// The Node app never registers a webhook itself, so an empty URL keeps that behavior.
public sealed class WebhookRegistrationService(
    ITelegramBotClient bot,
    IOptions<TelegramOptions> opts,
    ILogger<WebhookRegistrationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var url = opts.Value.WebhookUrl;
        if (string.IsNullOrEmpty(url))
        {
            logger.LogInformation("Telegram:WebhookUrl not set — assuming the webhook is registered externally.");
            return;
        }

        try
        {
            await bot.SetWebhook(url,
                allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery],
                cancellationToken: cancellationToken);
            logger.LogInformation("Telegram webhook registered: {Url}", url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register Telegram webhook {Url}", url);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
