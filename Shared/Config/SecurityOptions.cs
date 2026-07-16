using System.ComponentModel.DataAnnotations;

namespace MeroShareBot.Shared.Config;

public sealed class SecurityOptions
{
    [Required, MinLength(16)] public string DataEncryptionKey { get; init; } = "";
}
