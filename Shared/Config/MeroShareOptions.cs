using System.ComponentModel.DataAnnotations;

namespace MeroShareBot.Shared.Config;

public sealed class MeroShareOptions
{
    [Required] public string BaseUrl { get; init; } = "https://webbackend.cdsc.com.np/api";

    // Fallback default kitta for manual /apply when a linked account has no explicit amount set.
    // Autoapply always requires its own explicit kitta, so it never depends on this.
    [Range(1, 100)] public int DefaultApplyKitta { get; init; } = 10;
}
