using Unilyze.Pipeline;
namespace Unilyze.Metrics;

internal static class CodeHealthVersionSelector
{
    internal static double Score(TypeMetrics metrics, bool useV1)
        => useV1 ? metrics.CodeHealthV1 ?? metrics.CodeHealth : metrics.CodeHealth;

    internal static AnalysisResult Select(AnalysisResult result, bool useV1)
    {
        if (!useV1 || result.TypeMetrics is not { } metrics)
            return result;

        return result with
        {
            TypeMetrics = metrics
                .Select(type => type with { CodeHealth = Score(type, useV1) })
                .ToList(),
        };
    }
}
