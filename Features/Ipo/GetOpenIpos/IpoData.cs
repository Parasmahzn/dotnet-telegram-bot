namespace MeroShareBot.Features.Ipo;

public sealed record IpoData(
    string Name,
    string Symbol,
    string SubGroup,
    string Type,
    string ShareType);
