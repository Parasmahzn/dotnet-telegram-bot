using MeroShareBot.Shared.Telegram;
using Telegram.Bot.Types;

namespace MeroShareBot.Features.Help;

public sealed class HelpEndpoint(TelegramSender sender)
{
    private const string HelpText =
        "🤖 MeroShare Bot — Available commands:\n" +
        "\n" +
        "👤 /profile [1]    — View your profile and account details\n" +
        "📋 /ipo            — List currently open IPOs\n" +
        "🚀 /apply <name>   — Apply for an IPO (asks for confirmation)\n" +
        "❓ /help           — Show this message";

    public Task HandleStartAsync(Message msg) =>
        sender.SendTextAsync(msg.Chat.Id, "👋 Welcome to MeroShare Bot!\n\nType /help to see available commands.");

    public Task HandleHelpAsync(Message msg) =>
        sender.SendTextAsync(msg.Chat.Id, HelpText);
}
