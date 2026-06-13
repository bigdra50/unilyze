using Unilyze.Config;
using Unilyze.Pipeline;
using System.Text.Json;

namespace Unilyze.Findings;

internal static class TriageApplication
{
    public static string? ResolvePath(
        IReadOnlyDictionary<string, string> opts,
        UnilyzeConfig config,
        string projectRoot)
    {
        if (opts.ContainsKey("--no-triage"))
            return null;

        var explicitPath = opts.GetValueOrDefault("--triage") ?? config.Triage;
        if (explicitPath is not null)
            return TriageFile.ResolvePath(projectRoot, explicitPath);

        var defaultPath = TriageFile.DefaultPath(projectRoot);
        return File.Exists(defaultPath) ? defaultPath : null;
    }

    public static int? TryApply(AnalysisResult result, string? triagePath, out AnalysisResult updated)
    {
        updated = result;
        if (triagePath is null)
            return null;

        if (!File.Exists(triagePath))
        {
            Console.Error.WriteLine($"Triage file not found: {triagePath}");
            return 1;
        }

        try
        {
            var triage = TriageFile.Load(triagePath);
            TriageMatcher.WarnIfMetricsVersionMismatch(triage);
            updated = TriageMatcher.Apply(result, triage, out var stats);
            TriageMatcher.WriteSummary(stats);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
