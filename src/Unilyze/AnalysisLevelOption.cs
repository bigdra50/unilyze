namespace Unilyze;

// Parsing and presentation for the --level option (issue 17).
// CLI tokens (syntax|core|full|complete) map to the AnalysisLevel enum.
internal static class AnalysisLevelOption
{
    public static bool TryParse(string? value, out AnalysisLevel level)
    {
        level = AnalysisLevel.Complete;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "syntax":
                level = AnalysisLevel.SyntaxOnly;
                return true;
            case "core":
                level = AnalysisLevel.CoreEngine;
                return true;
            case "full":
                level = AnalysisLevel.FullEngine;
                return true;
            case "complete":
                level = AnalysisLevel.Complete;
                return true;
            default:
                return false;
        }
    }

    // Compact statusline marker shown when the level is below Complete (issue 16).
    public static string? StatuslineMarker(string? analysisLevel) =>
        analysisLevel switch
        {
            nameof(AnalysisLevel.SyntaxOnly) => "[syntax]",
            nameof(AnalysisLevel.CoreEngine) => "[core]",
            nameof(AnalysisLevel.FullEngine) => "[full]",
            _ => null
        };
}
