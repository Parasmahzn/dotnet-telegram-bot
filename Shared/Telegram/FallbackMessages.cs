namespace MeroShareBot.Shared.Telegram;

public static class FallbackMessages
{
    private static readonly string[] Messages =
    [
        "🤖 I'm a bot, not a chat partner! Try /help to see what I can do.",
        "💬 I don't do small talk. Type /help for commands.",
        "🙉 I only speak in commands. Try /ipo, /profile, or /help.",
        "🤷 Not sure what to do with that. Type /help to see my commands.",
        "📋 I'm more of a task bot. Check /help for what I can do!",
        "🚫 I don't chat — but I can check IPOs and profiles. Try /help.",
        "😅 That went over my circuits. Use /help to see what I understand.",
        "🤖 Beep boop. I only understand commands. Try /help!",
    ];

    public static string Random() => Messages[System.Random.Shared.Next(Messages.Length)];
}
