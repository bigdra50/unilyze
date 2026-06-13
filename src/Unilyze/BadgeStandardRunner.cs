namespace Unilyze;

internal static class BadgeStandardRunner
{
    internal sealed record Result(ShieldsBadge Badge, StatuslineFormatter.Summary Summary);

    internal static Result? TryAnalyze(
        string fullPath,
        UnilyzeConfig config,
        IReadOnlyDictionary<string, string> opts,
        BadgeMetric metric,
        AnalysisLevel? requestedLevel,
        string? baselinePath,
        bool useCodeHealthV1,
        out int exitCode)
    {
        exitCode = 0;
        var resolved = config.ResolveAnalysisConfig();
        var result = AnalysisPipeline.Build(
            fullPath, null, null, config.ExcludeDirs, requestedLevel,
            excludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
            applyAnyDepthExcludes: !config.DisableDefaultExcludes,
            analysisConfig: resolved,
            maxParallelism: config.MaxParallelism);

        var effectiveBaseline = baselinePath ?? config.Baseline;
        var baselineError = ProgramHelpers.TryApplyBaseline(result, fullPath, effectiveBaseline, out result);
        if (baselineError is 1)
        {
            exitCode = 1;
            return null;
        }

        var triagePath = TriageApplication.ResolvePath(opts, config, fullPath);
        var triageError = TriageApplication.TryApply(result, triagePath, out result);
        if (triageError is 1)
        {
            exitCode = 1;
            return null;
        }

        var excludeBaselined = effectiveBaseline is not null;
        var summary = StatuslineFormatter.ComputeSummary(result, excludeBaselined, useCodeHealthV1);
        return new Result(BadgeFormatter.Build(metric, summary), summary);
    }
}
