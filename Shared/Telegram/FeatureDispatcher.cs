using MeroShareBot.Features.Help;
using MeroShareBot.Features.Ipo.ApplyIpo;
using MeroShareBot.Features.Ipo.GetOpenIpos;
using MeroShareBot.Features.Profile;
using MeroShareBot.Shared.Config;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;

namespace MeroShareBot.Shared.Telegram;

// Port of src/bot/handleMessage.js — auth guard + command/callback routing.
public sealed class FeatureDispatcher(
    IServiceProvider sp,
    TelegramSender sender,
    IOptions<TelegramOptions> tgOpts)
{
    private static readonly HashSet<string> PublicCommands = ["start", "help", "ipo"];

    public async Task DispatchAsync(Update update)
    {
        var allowed = tgOpts.Value.AllowedChatIds;

        // Callbacks: only allowed chats; route by callback_data prefix
        if (update.CallbackQuery is { } cb)
        {
            var cbChatId = cb.Message?.Chat.Id;
            if (cbChatId is null || !allowed.Contains(cbChatId.Value)) return;

            var data = cb.Data ?? "";
            if (data.StartsWith("apply_"))
            {
                await sp.GetRequiredService<ApplyIpoEndpoint>().HandleCallbackAsync(cb);
            }
            else if (data.StartsWith("profile_"))
            {
                await sp.GetRequiredService<GetProfileEndpoint>().HandleCallbackAsync(cb);
            }
            return;
        }

        if (update.Message is not { } msg) return;

        var text = msg.Text ?? "";

        if (!text.StartsWith('/'))
        {
            await sender.SendTextAsync(msg.Chat.Id, FallbackMessages.Random());
            return;
        }

        var parts = text.Split(' ', 2);
        var cmd = parts[0][1..].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1].Trim() : "";

        // Public commands pass through; protected commands require an allowed chat ID
        if (!PublicCommands.Contains(cmd) && !allowed.Contains(msg.Chat.Id))
        {
            await sender.SendTextAsync(msg.Chat.Id, "🚫 You don't have access to this command. Contact the admin to get access.");
            return;
        }

        switch (cmd)
        {
            case "start":
                await sp.GetRequiredService<HelpEndpoint>().HandleStartAsync(msg);
                break;
            case "help":
                await sp.GetRequiredService<HelpEndpoint>().HandleHelpAsync(msg);
                break;
            case "profile":
                await sp.GetRequiredService<GetProfileEndpoint>().HandleMessageAsync(msg, arg);
                break;
            case "ipo":
                await sp.GetRequiredService<GetOpenIposEndpoint>().HandleAsync(msg);
                break;
            case "apply":
                await sp.GetRequiredService<ApplyIpoEndpoint>().HandleMessageAsync(msg, arg);
                break;
            default:
                await sender.SendTextAsync(msg.Chat.Id, FallbackMessages.Random());
                break;
        }
    }
}
