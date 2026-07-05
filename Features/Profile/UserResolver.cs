using MeroShareBot.Shared.Config;

namespace MeroShareBot.Features.Profile;

// Port of src/bot/commands/resolveUser.js — pick a user by 1-based index or username.
public static class UserResolver
{
    public static MeroShareUser? Resolve(IReadOnlyList<MeroShareUser> users, string arg)
    {
        if (users.Count == 0) return null;
        if (string.IsNullOrEmpty(arg)) return users.Count == 1 ? users[0] : null;

        if (int.TryParse(arg, out var index) && index >= 1 && index <= users.Count)
            return users[index - 1];

        return users.FirstOrDefault(u => u.Username == arg);
    }
}
