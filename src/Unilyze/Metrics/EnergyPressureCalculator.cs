using Unilyze.Detectors;
namespace Unilyze.Metrics;

internal readonly record struct EnergyPressureMetrics(
    int HotPathSmellCount,
    int HotPathMethodCount)
{
    internal double? Pressure => HotPathMethodCount > 0
        ? HotPathSmellCount / (double)HotPathMethodCount
        : null;
}

internal static class EnergyPressureCalculator
{
    internal static EnergyPressureMetrics ForGate(
        IReadOnlyList<TypeMetrics> metrics,
        bool excludeBaselined)
        => Calculate(metrics, smell => SmellAggregation.CountsForGate(smell, excludeBaselined));

    internal static EnergyPressureMetrics ForTrend(IReadOnlyList<TypeMetrics> metrics)
        => Calculate(metrics, SmellAggregation.CountsForTrend);

    static EnergyPressureMetrics Calculate(
        IReadOnlyList<TypeMetrics> metrics,
        Func<CodeSmell, bool> counts)
    {
        var methodCount = metrics.Sum(metric => metric.HotPathMethodCount ?? 0);
        var smellCount = metrics.Sum(metric =>
            metric.CodeSmells?.Count(smell => smell.InHotPath == true && counts(smell)) ?? 0);
        return new EnergyPressureMetrics(smellCount, methodCount);
    }
}
