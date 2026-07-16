namespace MeroShareBot.Features.Portfolio;

public sealed record PortfolioResult(bool Success, PortfolioResponse? Portfolio = null, string? Error = null);
