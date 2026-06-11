using System.Diagnostics;

namespace Unilyze;

internal static class AnalysisPipeline
{
    // requestedLevel pins the analysis level (issue 17):
    //   - resolved level above the request is capped down deterministically
    //   - resolved level below the request fails (the pipeline does not silently degrade)
    // When null the level is auto-resolved as before, so existing callers are unaffected.
    public static AnalysisResult Build(
        string path, string? prefix, string? assemblyFilter,
        IReadOnlyList<string>? excludeDirectories = null,
        AnalysisLevel? requestedLevel = null,
        bool excludeGeneratedCode = true,
        bool applyAnyDepthExcludes = true,
        IAnalysisLogSink? logSink = null,
        EffectiveSmellThresholds? thresholds = null,
        IReadOnlySet<CodeSmellKind>? disabledRuleKinds = null,
        bool disableCycles = false)
    {
        var options = new AnalysisBuildOptions(
            path, prefix, assemblyFilter, excludeDirectories, requestedLevel,
            excludeGeneratedCode, applyAnyDepthExcludes, logSink, thresholds, disabledRuleKinds, disableCycles);
        return Build(options);
    }

    static AnalysisResult Build(AnalysisBuildOptions options)
    {
        var log = options.EffectiveLog;
        var sw = Stopwatch.StartNew();

        log.PhaseStarted("discover");
        var discover = AnalysisPipelineDiscovery.Discover(options);
        log.PhaseCompleted("discover", sw.Elapsed);
        sw.Restart();

        log.PhaseStarted("parse");
        var (allTypes, allSyntaxTrees) = AnalysisPipelineDiscovery.CollectTypes(discover, options);
        log.PhaseCompleted("parse", sw.Elapsed);
        sw.Restart();

        log.PhaseStarted("compile");
        var compile = AnalysisPipelineDiscovery.Compile(options, discover, allSyntaxTrees, log);
        log.PhaseCompleted("compile", sw.Elapsed);
        sw.Restart();

        log.PhaseStarted("semantic");
        var (resolvedTypes, deps, typeMetrics, couplingMap) = AnalysisPipelineSemanticPhase.Run(
            allTypes, allSyntaxTrees, compile.CompilationResult, options);
        log.PhaseCompleted("semantic", sw.Elapsed);
        sw.Restart();

        log.PhaseStarted("aggregate");
        var (finalMetrics, assemblyInfos, cycles) = AnalysisPipelineAggregation.Run(
            discover, resolvedTypes, deps, typeMetrics, couplingMap, options.DisableCycles);
        log.PhaseCompleted("aggregate", sw.Elapsed);

        return new AnalysisResult(
            Path.GetFullPath(options.Path),
            DateTimeOffset.UtcNow,
            assemblyInfos,
            resolvedTypes,
            deps,
            finalMetrics,
            compile.AnalysisLevel,
            cycles,
            AnalysisResult.CurrentMetricsVersion,
            ToolVersionInfo.Current,
            ProjectKind: discover.ProjectKind);
    }
}
