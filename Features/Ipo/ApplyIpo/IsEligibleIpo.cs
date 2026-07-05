namespace MeroShareBot.Features.Ipo.ApplyIpo;

public static class IsEligibleIpo
{
    // Type=IPO and share=Ordinary are the criteria for a regular IPO application.
    public static bool Check(IpoData ipo) =>
        string.Equals(ipo.Type, "ipo", StringComparison.OrdinalIgnoreCase)
        && ipo.ShareType.Contains("ordinary", StringComparison.OrdinalIgnoreCase);
}
