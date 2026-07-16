namespace MeroShareBot.Shared.Data.Entities;

// Per-chat settings that aren't tied to a single linked account: which account is the default,
// whether the chat opted into daily IPO notifications.
public sealed class ChatSettingsEntity
{
    public long ChatId { get; set; }
    public Guid? DefaultAccountId { get; set; }
    public bool NotifyEnabled { get; set; }
}
