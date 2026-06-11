using System.Text;

namespace Unilyze;

internal static class McpAnalyzeSummary
{
    internal sealed record SmellCounts(int Warning, int Critical, int Informational);

    public static string ToMarkdown(AnalysisResult result)
    {
        var summary = StatuslineFormatter.ComputeSummary(result);
        var smells = CountSmells(result);
        var sb = new StringBuilder();
        sb.AppendLine("# Analysis Summary");
        sb.AppendLine();
        sb.AppendLine($"Project: `{result.ProjectPath}`");
        sb.AppendLine(
            $"Types: {summary.TypeCount} | Assemblies: {result.Assemblies.Count}");
        sb.AppendLine(
            $"Code Health: avg {summary.AverageCodeHealth:F1} / min {summary.MinCodeHealth:F1}");
        sb.AppendLine(
            $"Smells: {smells.Warning} warning, {smells.Critical} critical, {smells.Informational} informational");
        if (result.AnalysisLevel is not null)
            sb.AppendLine($"Level: {result.AnalysisLevel}");
        if (result.Profile is not null)
            sb.AppendLine($"Profile: {result.Profile}");
        var metricsVersion = result.MetricsVersion == 0
            ? AnalysisResult.CurrentMetricsVersion
            : result.MetricsVersion;
        var toolVersion = result.ToolVersion ?? ToolVersionInfo.Current;
        sb.AppendLine($"metricsVersion: {metricsVersion} | toolVersion: {toolVersion}");
        if (result.SuppressedCount is > 0)
            sb.AppendLine($"suppressedCount: {result.SuppressedCount}");
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    static SmellCounts CountSmells(AnalysisResult result)
    {
        var warning = 0;
        var critical = 0;
        var informational = 0;
        foreach (var type in result.TypeMetrics ?? [])
        {
            if (type.CodeSmells is { Count: > 0 })
            {
                foreach (var smell in type.CodeSmells)
                {
                    switch (smell.Severity)
                    {
                        case SmellSeverity.Warning: warning++; break;
                        case SmellSeverity.Critical: critical++; break;
                    }
                }
            }

            informational += type.InformationalCount ?? 0;
        }

        return new SmellCounts(warning, critical, informational);
    }
}
