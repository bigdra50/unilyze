using System.Text.Json;

namespace Unilyze;

public sealed record TrendSnapshot(
    DateTimeOffset AnalyzedAt,
    string ProjectPath,
    int TypeCount,
    double AverageCodeHealth,
    double MinCodeHealth,
    int CodeSmellCount,
    int HighComplexityTypeCount,
    double AverageCognitiveComplexity);

public sealed record TrendSummary(
    int SnapshotCount,
    double CodeHealthDelta,
    int CodeSmellDelta);

public sealed record TrendResult(
    IReadOnlyList<TrendSnapshot> Snapshots,
    TrendSummary Summary);

public static class TrendAnalyzer
{
    public static TrendSnapshot ToSnapshot(AnalysisResult result)
    {
        var typeMetrics = result.TypeMetrics ?? [];

        var typeCount = typeMetrics.Count;
        var avgHealth = typeCount > 0 ? Math.Round(typeMetrics.Average(t => t.CodeHealth), 1) : 0.0;
        var minHealth = typeCount > 0 ? Math.Round(typeMetrics.Min(t => t.CodeHealth), 1) : 0.0;
        var smellCount = typeMetrics.Sum(t => t.CodeSmells?.Count ?? 0);
        var highComplexity = typeMetrics.Count(t => t.CodeHealth < 4.0);
        var avgCogCC = typeCount > 0
            ? Math.Round(typeMetrics.Average(t => t.AverageCognitiveComplexity), 1)
            : 0.0;

        return new TrendSnapshot(
            result.AnalyzedAt,
            result.ProjectPath,
            typeCount,
            avgHealth,
            minHealth,
            smellCount,
            highComplexity,
            avgCogCC);
    }

    public static TrendResult Analyze(IReadOnlyList<AnalysisResult> results)
    {
        if (results.Count == 0)
        {
            return new TrendResult(
                [],
                new TrendSummary(0, 0.0, 0));
        }

        var snapshots = results
            .Select(ToSnapshot)
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
