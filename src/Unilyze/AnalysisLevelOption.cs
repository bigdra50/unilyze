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
                level = AnalysisLevel.Syntax;
                return true;
            case "core":
                level = AnalysisLevel.Core;
                return true;
            case "full":
                level = AnalysisLevel.Full;
                return true;
            case "complete":
                level = AnalysisLevel.Complete;
                return true;
            default:
                return false;
        }
    }

    // External JSON/badge/statusline vocabulary (issue 16/72): keep legacy names stable.
    public static string ToExternalName(AnalysisLevel level) =>
        level switch
        {
            AnalysisLevel.Syntax => "SyntaxOnly",
            AnalysisLevel.Core => "CoreEngine",
            AnalysisLevel.Full => "FullEngine",
            AnalysisLevel.Complete => "Complete",
            _ => level.ToString()
        };

    // Compact statusline marker shown when the level is below Complete (issue 16).
    public static string? StatuslineMarker(string? analysisLevel) =>
        analysisLevel switch
        {
            "SyntaxOnly" => "[syntax]",
            "CoreEngine" => "[core]",
            "FullEngine" => "[full]",
            _ => null
        };
}
