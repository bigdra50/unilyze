using System.Text;

namespace Unilyze;

internal static class StatuslineFormatter
{
    internal sealed record Summary(
        double AverageCodeHealth,
        double MinCodeHealth,
        int WarningCount,
        int CriticalCount,
        int TypeCount,
        double AverageMaintainabilityIndex,
        int BoxingCount,
        int CyclicDependencyCount,
        int MiBearingCount = 0,
        int HotPathSmellCount = 0,
        int HotPathMethodCount = 0,
        string? AnalysisLevel = null);

    internal static Summary ComputeSummary(AnalysisResult result, bool excludeBaselined = false)
    {
        var metrics = result.TypeMetrics ?? [];
        if (metrics.Count == 0)
            return new Summary(
                0.0, 0.0, 0, 0, 0, 0.0, 0, 0,
                AnalysisLevel: result.AnalysisLevel);

        var avg = Math.Round(metrics.Average(t => t.CodeHealth), 1);
        var min = Math.Round(metrics.Min(t => t.CodeHealth), 1);
        var warnings = metrics.Sum(t =>
            t.CodeSmells?.Count(s => s.Severity == SmellSeverity.Warning && CountForSummary(s, excludeBaselined)) ?? 0);
        var criticals = metrics.Sum(t =>
            t.CodeSmells?.Count(s => s.Severity == SmellSeverity.Critical && CountForSummary(s, excludeBaselined)) ?? 0);
        // MI is undefined for method-less types (records, markers) — exclude them
        // from the denominator instead of counting them as 0.
        var miValues = metrics
            .Where(t => t.AverageMaintainabilityIndex is not null)
            .Select(t => t.AverageMaintainabilityIndex!.Value)
            .ToList();
        var avgMi = miValues.Count > 0 ? Math.Round(miValues.Average(), 1) : 0.0;
        var boxing = metrics.Sum(t => t.BoxingCount ?? 0);
        var cyclicDeps = result.CyclicDependencies?.Count ?? 0;
        var energy = EnergyPressureCalculator.ForGate(metrics, excludeBaselined);

        return new Summary(
            avg,
            min,
            warnings,
            criticals,
            metrics.Count,
            avgMi,
            boxing,
            cyclicDeps,
            miValues.Count,
            energy.HotPathSmellCount,
            energy.HotPathMethodCount,
            result.AnalysisLevel);
    }

    static bool CountForSummary(CodeSmell smell, bool excludeBaselined)
        => SmellAggregation.CountsForGate(smell, excludeBaselined);

    internal static string Format(Summary s)
    {
        if (s.TypeCount == 0)
            return "";

        const string Reset = "\x1b[0m";
        const string Green = "\x1b[32m";
        const string Yellow = "\x1b[33m";
        const string Red = "\x1b[31m";
        const string Cyan = "\x1b[36m";

        var healthColor = s.AverageCodeHealth switch
        {
            >= 8.0 => Green,
            >= 5.0 => Yellow,
            _ => Red
        };

        var minHealthColor = s.MinCodeHealth switch
        {
            >= 8.0 => Green,
            >= 5.0 => Yellow,
            _ => Red
        };

        var miColor = s.AverageMaintainabilityIndex switch
        {
            >= 80.0 => Green,
            >= 60.0 => Yellow,
            _ => Red
        };

        var sb = new StringBuilder();

        // Code Health (avg/min)
        sb.Append($"{healthColor}CH:{s.AverageCodeHealth:F1}{Reset}");
        sb.Append($"/{minHealthColor}{s.MinCodeHealth:F1}{Reset}");

        // Maintainability Index
        sb.Append($" {miColor}MI:{s.AverageMaintainabilityIndex:F0}{Reset}");

        // Smells
        sb.Append($" {Yellow}{s.WarningCount}smells{Reset}");
        if (s.CriticalCount > 0)
            sb.Append($" {Red}\U0001f534{s.CriticalCount}{Reset}");

        // Boxing
        if (s.BoxingCount > 0)
            sb.Append($" {Cyan}\U0001f4e6{s.BoxingCount}{Reset}");

        // Cyclic dependencies
        if (s.CyclicDependencyCount > 0)
            sb.Append($" {Red}\u267b{s.CyclicDependencyCount}{Reset}");

        // Analysis-level marker: shown only when below Complete so degraded runs are visible (issue 16).
        var marker = AnalysisLevelOption.StatuslineMarker(s.AnalysisLevel);
        if (marker is not null)
            sb.Append($" {Yellow}{marker}{Reset}");

        return sb.ToString();
    }
}
