namespace Unilyze.Findings;

internal static class TriageVerdicts
{
    public const string Confirmed = "confirmed";
    public const string FalsePositive = "false-positive";
    public const string WontFix = "wontfix";

    public static readonly string[] All = [Confirmed, FalsePositive, WontFix];

    public static bool IsKnown(string? verdict)
        => verdict is Confirmed or FalsePositive or WontFix;

    public static bool ExcludesFromGates(string? verdict)
        => verdict is FalsePositive or WontFix;

    public static bool ExcludesFromTrend(string? verdict)
        => verdict is FalsePositive;
}
