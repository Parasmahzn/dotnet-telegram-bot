namespace MeroShareBot.Features.Ipo;

public sealed record IpoData(
    int CompanyShareId,
    string Name,
    string Symbol,
    string SubGroup,
    string Type,
    string ShareType);
