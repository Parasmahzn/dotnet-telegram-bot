namespace MeroShareBot.Shared.MeroShare;

// The API client only ever depends on these two records — never on MeroShareOptions/MeroShareUser/
// LinkedAccount — so account storage can change shape without touching the client.
public sealed record MeroShareCredentials(string Username, string Password, string Dp);

public sealed record MeroShareApplyCredentials(string Crn, string Pin);
