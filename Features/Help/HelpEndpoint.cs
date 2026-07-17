namespace MeroShareBot.Features.Help;

public sealed class HelpEndpoint(TelegramSender sender)
{
    private const string HelpText =
        "🤖 MeroShare Bot — Available commands:\n" +
        "\n" +
        "🔗 /login              — Link a MeroShare account\n" +
        "📇 /accounts           — List linked accounts\n" +
        "🔀 /switch <n>         — Set default account\n" +
        "🗑️ /removeaccount <n>  — Remove linked account\n" +
        "👤 /profile [n]        — View MeroShare profile\n" +
        "📊 /portfolio [n]      — View share holdings\n" +
        "📈 /market [sym]       — NEPSE index / quote (coming soon)\n" +
        "👀 /watch              — Symbol watchlist\n" +
        "📋 /ipo [n]            — List open IPOs (default account)\n" +
        "🚀 /apply <name>       — Apply for an IPO\n" +
        "🤖 /autoapply          — Auto-apply settings (always confirm-tap)\n" +
        "🔔 /notify             — IPO availability notifications\n" +
        "⚙️ /settings           — Settings hub\n" +
        "👥 /users              — List registered chats (admin only)\n" +
        "📢 /broadcast          — Send a message to all chats (admin only)\n" +
        "❓ /help               — Show this message";

    public Task HandleStartAsync(Message msg) =>
        sender.SendTextAsync(msg.Chat.Id,
            "👋 Welcome to MeroShare Bot!\n\n" +
            "I automate MeroShare — Nepal's share application portal. Link your account, check open IPOs, and apply in a couple of taps.\n\n" +
            "🔗 /login to link your MeroShare account\n" +
            "❓ /help to see everything I can do");

    public Task HandleHelpAsync(Message msg) =>
        sender.SendTextAsync(msg.Chat.Id, HelpText);
}
