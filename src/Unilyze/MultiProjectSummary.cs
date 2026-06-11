using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unilyze;

internal sealed record MultiProjectSummaryEntry(
    string Name,
    string Path,
    string? AnalysisLevel,
    int MetricsVersion,
    double CodeHealthMin,
    double CodeHealthAvg,
    int CriticalCount,
    int WarningCount,
    string? Gate = null);

internal sealed record MultiProjectSummaryDocument(
    string ToolVersion,
    IReadOnlyList<MultiProjectSummaryEntry> Projects);

internal static class MultiProjectSummary
{
    public static MultiProjectSummaryEntry FromAnalysis(
        string name,
        string path,
        AnalysisResult result,
        StatuslineFormatter.Summary summary,
        string? gate = null)
        => new(
            name,
            path,
            result.AnalysisLevel,
            result.MetricsVersion,
            summary.MinCodeHealth,
            summary.AverageCodeHealth,
            summary.CriticalCount,
            summary.WarningCount,
            gate);

    public static string Serialize(MultiProjectSummaryDocument document)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        return JsonSerializer.Serialize(document, options);
    }

    public static void PrintBadgeTable(IReadOnlyList<(MultiProjectSummaryEntry Entry, string MetricValue)> rows)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("| Project | Value | Gate |");
        Console.Error.WriteLine("|---------|-------|------|");
        foreach (var (entry, value) in rows)
            Console.Error.WriteLine($"| {entry.Name} | {value} | {entry.Gate} |");
    }

    public static string FormatBadgeMetricValue(BadgeMetric metric, StatuslineFormatter.Summary summary)
    {
        if (summary.TypeCount == 0)
            return "n/a";

        return metric switch
        {
            BadgeMetric.CodeHealth => string.Format(
                CultureInfo.InvariantCulture,
                "{0:F1} / {1:F1}",
                summary.AverageCodeHealth,
                summary.MinCodeHealth),
            BadgeMetric.Mi => summary.AverageMaintainabilityIndex.ToString("F0", CultureInfo.InvariantCulture),
            BadgeMetric.Smells => summary.WarningCount.ToString(CultureInfo.InvariantCulture),
            _ => "n/a",
        };
    }

    public static string FormatGateOutcome(BadgeGateResult gate)
        => gate.Outcome switch
        {
            GateOutcome.Pass => "pass",
            GateOutcome.Fail => "fail",
            _ => "error",
        };
}
