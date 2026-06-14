using Unilyze.Findings;
using Unilyze.Discovery;
using Unilyze.Config;
using Unilyze.Detectors;
using Unilyze.Metrics;
using Unilyze.Incremental;
using Unilyze.Cli;
using System.Diagnostics;

namespace Unilyze.Pipeline;

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
        ResolvedAnalysisConfig? analysisConfig = null,
        int? maxParallelism = null,
        bool resolveNuget = false,
        bool includeGenerated = false,
        string? targetFramework = null,
        bool incremental = false)
    {
        var options = new AnalysisBuildOptions(
            path, prefix, assemblyFilter, excludeDirectories, requestedLevel,
            excludeGeneratedCode, applyAnyDepthExcludes, includeApiSurface, logSink, analysisConfig,
            maxParallelism, resolveNuget, includeGenerated, targetFramework, incremental);
        return Build(options);
    }

    static AnalysisResult Build(AnalysisBuildOptions options)
    {
        if (options.Incremental && options.RequestedLevel != AnalysisLevel.Syntax)
        {
            options.EffectiveLog.Warning(
                "--incremental currently accelerates syntax-level analysis only; running full analysis");
            options = options with { Incremental = false };
        }

        try
        {
            return BuildCore(options);
        }
        finally
        {
            SyntaxIncrementalState.Current = null;
        }
    }

    static AnalysisResult BuildCore(AnalysisBuildOptions options)
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
        var referenceOnlyTrees = AnalysisPipelineDiscovery.CollectReferenceOnlyTrees(
            discover, options, allSyntaxTrees);
        log.PhaseCompleted("parse", sw.Elapsed);
        sw.Restart();

        log.PhaseStarted("compile");
        var compile = AnalysisPipelineDiscovery.Compile(
            options, discover, allSyntaxTrees, referenceOnlyTrees, log);
        log.PhaseCompleted("compile", sw.Elapsed);
        sw.Restart();

        log.PhaseStarted("semantic");
        List<TypeNodeInfo> resolvedTypes;
        List<TypeDependency> deps;
        List<TypeMetrics> typeMetrics;
        IReadOnlyDictionary<string, CouplingInfo> couplingMap;
        if (options.UseSyntaxIncrementalCache && SyntaxIncrementalState.Current is { } collect)
        {
            (resolvedTypes, deps, typeMetrics, couplingMap) = SyntaxIncrementalSemanticPhase.Run(
                allTypes, allSyntaxTrees, compile.CompilationResult, options, collect, discover);
        }
        else
        {
            (resolvedTypes, deps, typeMetrics, couplingMap) = AnalysisPipelineSemanticPhase.Run(
                allTypes, allSyntaxTrees, compile.CompilationResult, options, discover);
        }
        log.PhaseCompleted("semantic", sw.Elapsed);
        sw.Restart();

        log.PhaseStarted("aggregate");
        var (finalMetrics, assemblyInfos, cycles) = AnalysisPipelineAggregation.Run(
            discover, resolvedTypes, deps, typeMetrics, couplingMap, config.DisableCycles);
        log.PhaseCompleted("aggregate", sw.Elapsed);

        if (options.UseSyntaxIncrementalCache && SyntaxIncrementalState.Current is { } incrementalCollect)
        {
            var manifest = SyntaxIncrementalCollector.BuildManifest(
                discover.ProjectRoot, incrementalCollect, finalMetrics, resolvedTypes);
            SyntaxCacheStore.Save(discover.ProjectRoot, manifest);
        }

        var profileField = config.Profile == SmellThresholdProfiles.DefaultProfileName
            ? null
            : config.Profile;

        var selectedTfm = options.IncludeGenerated || options.ResolveNuget
            ? discover.SelectedTargetFramework ?? options.TargetFramework
            : null;

        var inlineSuppressedCount = InlineSuppression.CountSuppressed(finalMetrics);
        InlineSuppression.WriteSummary(inlineSuppressedCount);
        var energyPressure = discover.ProjectKind == "unity"
            ? EnergyPressureCalculator.ForGate(finalMetrics, excludeBaselined: false).Pressure
            : null;

        var (sourceTable, typesWithFileRefs) = BuildSourceTableAndFixRefs(resolvedTypes);

        var result = new AnalysisResult(
            Path.GetFullPath(options.Path),
            DateTimeOffset.UtcNow,
            assemblyInfos,
            typesWithFileRefs,
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
                : null,
            ResolveNuget: options.ResolveNuget ? true : null,
            IncludeGenerated: options.IncludeGenerated ? true : null,
            TargetFramework: selectedTfm,
            EnergyPressure: energyPressure,
            SchemaVersion: AnalysisResult.CurrentSchemaVersion,
            SourceTable: sourceTable.Count > 0 ? sourceTable : null);

        return FindingFingerprint.AssignIds(result);
    }

    static (List<string> Table, IReadOnlyList<TypeNodeInfo> Types) BuildSourceTableAndFixRefs(
        IReadOnlyList<TypeNodeInfo> types)
    {
        var pathToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (!string.IsNullOrEmpty(type.FilePath))
                pathToIndex.TryAdd(type.FilePath, pathToIndex.Count);
            foreach (var m in type.Members)
                if (!string.IsNullOrEmpty(m.SourceFile))
                    pathToIndex.TryAdd(m.SourceFile, pathToIndex.Count);
            if (type.Declarations is not null)
                foreach (var d in type.Declarations)
                    if (!string.IsNullOrEmpty(d.SourceFile))
                        pathToIndex.TryAdd(d.SourceFile, pathToIndex.Count);
        }

        var table = new string[pathToIndex.Count];
        foreach (var (path, index) in pathToIndex)
            table[index] = path;

        int ResolveRef(string? path) =>
            !string.IsNullOrEmpty(path) && pathToIndex.TryGetValue(path, out var idx) ? idx : 0;

        var updated = types.Select(type =>
        {
            var members = type.Members.Select(m =>
            {
                var memberFileRef = ResolveRef(m.SourceFile ?? type.FilePath);
                return m.Location is not null
                    ? m with { Location = m.Location with { FileRef = memberFileRef }, SourceFile = null }
                    : m with { SourceFile = null };
            }).ToList();

            var declarations = type.Declarations?.Select(d =>
                d with { FileRef = ResolveRef(d.SourceFile ?? type.FilePath), SourceFile = null }
            ).ToList();

            return type with { Members = members, Declarations = declarations };
        }).ToList();

        return ([..table], updated);
    }
}
