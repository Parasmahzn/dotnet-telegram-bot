using System.ComponentModel.DataAnnotations;

namespace MeroShareBot.Shared.Config;

public sealed class MeroShareOptions
{
    [Required, Url] public string LoginUrl { get; init; } = "";

    [Range(1, 100)] public int DefaultApplyKitta { get; init; } = 10;

    public List<MeroShareUser> Users { get; init; } = [];

    public string BaseUrl => LoginUrl.Split('#')[0];
}

public sealed class MeroShareUser
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string Dp { get; init; } = "";
    public string Crn { get; init; } = "";
    public string Pin { get; init; } = "";
}
