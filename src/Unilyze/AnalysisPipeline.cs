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
        bool includeApiSurface = false,
        IAnalysisLogSink? logSink = null,
        ResolvedAnalysisConfig? analysisConfig = null)
    {
        var options = new AnalysisBuildOptions(
            path, prefix, assemblyFilter, excludeDirectories, requestedLevel,
            excludeGeneratedCode, applyAnyDepthExcludes, includeApiSurface, logSink, analysisConfig);
        return Build(options);
    }

    static AnalysisResult Build(AnalysisBuildOptions options)
    {
        var config = options.EffectiveAnalysisConfig;
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
            discover, resolvedTypes, deps, typeMetrics, couplingMap, config.DisableCycles);
        log.PhaseCompleted("aggregate", sw.Elapsed);

        var profileField = config.Profile == SmellThresholdProfiles.DefaultProfileName
            ? null
            : config.Profile;

        var inlineSuppressedCount = InlineSuppression.CountSuppressed(finalMetrics);
        InlineSuppression.WriteSummary(inlineSuppressedCount);

        var result = new AnalysisResult(
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
            ProjectKind: discover.ProjectKind,
            Profile: profileField,
            SuppressedCount: inlineSuppressedCount > 0 ? inlineSuppressedCount : null,
            ApiSurface: options.IncludeApiSurface
                ? ApiSurfaceExtractor.Extract(allSyntaxTrees, resolvedTypes)
                : null);

        return FindingFingerprint.AssignIds(result);
    }
}
