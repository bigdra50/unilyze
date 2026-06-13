using Unilyze.Detectors;
using Unilyze.Metrics;
using Unilyze.Pipeline;
using System.Text.Json;

namespace Unilyze.History;

internal sealed record TrendSnapshot(
    DateTimeOffset AnalyzedAt,
    string ProjectPath,
    int TypeCount,
    double AverageCodeHealth,
    double MinCodeHealth,
    int CodeSmellCount,
    int HighComplexityTypeCount,
    double AverageCognitiveComplexity,
    string? SourceFile = null,
    int MetricsVersion = 0,
    string? Profile = null,
    int WarningSmellCount = 0,
    int CriticalSmellCount = 0,
    int HotPathSmellCount = 0,
    int HotPathMethodCount = 0);

internal sealed record TrendSummary(
    int SnapshotCount,
    double CodeHealthDelta,
    int CodeSmellDelta);

internal sealed record TrendResult(
    IReadOnlyList<TrendSnapshot> Snapshots,
    TrendSummary Summary);

internal static class TrendAnalyzer
{
    public static TrendSnapshot ToSnapshot(AnalysisResult result) =>
        ToSnapshot(result, sourceFile: null);

    public static TrendSnapshot ToSnapshot(AnalysisResult result, string? sourceFile)
    {
        var typeMetrics = result.TypeMetrics ?? [];

        var typeCount = typeMetrics.Count;
        var avgHealth = typeCount > 0 ? Math.Round(typeMetrics.Average(t => t.CodeHealth), 1) : 0.0;
        var minHealth = typeCount > 0 ? Math.Round(typeMetrics.Min(t => t.CodeHealth), 1) : 0.0;
        var smellCount = typeMetrics.Sum(t =>
            t.CodeSmells?.Count(s => SmellAggregation.CountsForTrend(s)) ?? 0);
        var warningCount = typeMetrics.Sum(t =>
            t.CodeSmells?.Count(s => s.Severity == SmellSeverity.Warning) ?? 0);
        var criticalCount = typeMetrics.Sum(t =>
            t.CodeSmells?.Count(s => s.Severity == SmellSeverity.Critical) ?? 0);
        var highComplexity = typeMetrics.Count(t => t.CodeHealth < 4.0);
        var avgCogCC = typeCount > 0
            ? Math.Round(typeMetrics.Average(t => t.AverageCognitiveComplexity), 1)
            : 0.0;
        var energy = EnergyPressureCalculator.ForTrend(typeMetrics);

        return new TrendSnapshot(
            result.AnalyzedAt,
            result.ProjectPath,
            typeCount,
            avgHealth,
            minHealth,
            smellCount,
            highComplexity,
            avgCogCC,
            sourceFile,
            result.MetricsVersion,
            result.Profile,
            warningCount,
            criticalCount,
            energy.HotPathSmellCount,
            energy.HotPathMethodCount);
    }

    public static TrendResult Analyze(IReadOnlyList<AnalysisResult> results) =>
        AnalyzeSnapshots(results.Select(r => ((string?)null, r)).ToList());

    public static TrendResult AnalyzeSnapshots(IReadOnlyList<(string? SourceFile, AnalysisResult Result)> entries)
    {
        if (entries.Count == 0)
        {
            return new TrendResult(
                [],
                new TrendSummary(0, 0.0, 0));
        }

        var snapshots = entries
            .Select(e => ToSnapshot(e.Result, e.SourceFile))
            .OrderBy(s => s.AnalyzedAt)
            .ToList();

        var first = snapshots[0];
        var last = snapshots[^1];

        var summary = new TrendSummary(
            snapshots.Count,
            Math.Round(last.AverageCodeHealth - first.AverageCodeHealth, 1),
            last.CodeSmellCount - first.CodeSmellCount);

        return new TrendResult(snapshots, summary);
    }
}
