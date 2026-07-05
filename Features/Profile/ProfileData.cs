namespace MeroShareBot.Features.Profile;

public sealed record ProfilePersonal(
    string Boid,
    string Name,
    string Gender,
    string Email,
    string Phone,
    string Address,
    string? Username);

public sealed record ProfileAccountEntry(string Label, string Value);

public sealed record ProfileResult(
    bool Success,
    ProfilePersonal? Personal = null,
    IReadOnlyList<ProfileAccountEntry>? Account = null,
    string? Error = null);
