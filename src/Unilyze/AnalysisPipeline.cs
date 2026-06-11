using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
        thresholds ??= EffectiveSmellThresholds.Default;
        disabledRuleKinds ??= new HashSet<CodeSmellKind>();
        var log = logSink ?? new ConsoleAnalysisLogSink(quiet: false);
        var sw = Stopwatch.StartNew();

        log.PhaseStarted("discover");
        var assetsDir = ProgramHelpers.ResolveAssetsDir(path);
        var asmdefs = AsmdefInfo.Discover(assetsDir, excludeDirectories, excludeGeneratedCode, applyAnyDepthExcludes);

        IReadOnlyList<AsmdefInfo> targets;
        if (asmdefs.Count == 0)
        {
            targets = [new AsmdefInfo("Assembly-CSharp", assetsDir, [])];
        }
        else
        {
            prefix ??= ProgramHelpers.DetectCommonPrefix(asmdefs);
            targets = ProgramHelpers.FilterAssemblies(asmdefs, prefix, assemblyFilter);
        }

        var projectRoot = ProgramHelpers.ResolveProjectRoot(path);
        var csprojInfo = ResolveCsprojInfo(projectRoot, excludeDirectories, log);

        // Cap DLL collection at the requested level so the pin is deterministic.
        var cap = requestedLevel ?? AnalysisLevel.Complete;
        var resolved = UnityDllResolver.Resolve(projectRoot, cap);
        var preprocessorSymbols = MergePreprocessorSymbols(projectRoot, csprojInfo);
        log.PhaseCompleted("discover", sw.Elapsed);
        sw.Restart();

        log.PhaseStarted("parse");
        var (allTypes, allSyntaxTrees) = CollectTypes(
            targets, preprocessorSymbols, excludeDirectories, excludeGeneratedCode, applyAnyDepthExcludes);
        log.PhaseCompleted("parse", sw.Elapsed);
        sw.Restart();

        log.PhaseStarted("compile");
        var compilationResult = CompilationFactory.Create(resolved, allSyntaxTrees, csprojInfo, cap, log);
        var analysisLevel = AnalysisLevelOption.ToExternalName(compilationResult.Level);

        // The level is always reported on stderr (issue 16: previously only when != SyntaxOnly).
        log.Info($"Analysis level: {analysisLevel}");

        var projectKind = ProgramHelpers.ResolveProjectKind(projectRoot);

        // A Unity project that silently fell back to SyntaxOnly means DLL resolution
        // failed and semantic metrics (boxing/CBO/DIT/...) are understated (issue 16).
        if (projectKind == "unity" && compilationResult.Level == AnalysisLevel.Syntax
            && requestedLevel is not AnalysisLevel.Syntax)
        {
            log.Warning(
                "Warning: Unity project detected but Unity DLLs could not be resolved; "
                + "analysis degraded to SyntaxOnly. Semantic metrics (boxing, CBO, DIT, etc.) are understated.");
        }

        // Pin requested an analysis depth the environment cannot satisfy: fail loudly (issue 17).
        if (requestedLevel is { } required && compilationResult.Level < required)
        {
            throw new InvalidOperationException(
                $"Requested analysis level '{required}' could not be satisfied "
                + $"(resolved '{compilationResult.Level}'). Unity DLLs may be missing.");
        }
        log.PhaseCompleted("compile", sw.Elapsed);
        sw.Restart();

        log.PhaseStarted("semantic");
        allTypes = BaseTypeResolver.ResolveTypeRelationships(allTypes, allSyntaxTrees, compilationResult).ToList();

        var deps = DependencyBuilder.Build(allTypes).ToList();

        // DI container dependency detection.
        // Resolve registration endpoints to TypeIds where possible so the edges
        // integrate with the graph, cycle detection, CBO/Ca/Ce, and TypeRank.
        // External or ambiguous endpoints stay null (FromTypeId/ToTypeId), which
        // downstream consumers treat as an unresolved edge (no metric contribution).
        var diRegistrations = DIContainerAnalyzer.Analyze(allSyntaxTrees, compilationResult.Compilation);
        var diTypeIndex = DITypeIdIndex.Build(allTypes);
        foreach (var reg in diRegistrations)
        {
            var fromTypeId = diTypeIndex.Resolve(reg.ServiceType, reg.ServiceTypeQualified);
            var toTypeId = diTypeIndex.Resolve(reg.ImplementationType, reg.ImplementationTypeQualified);
            deps.Add(new TypeDependency(
                reg.ServiceType, reg.ImplementationType, DependencyKind.DIRegistration, fromTypeId, toTypeId));
        }

        var typeMetrics = CodeHealthCalculator.ComputeTypeMetrics(allTypes);

        var couplingMap = CouplingMetricsCalculator.Calculate(deps, allTypes);
        typeMetrics = EnrichWithCouplingMetrics(typeMetrics, couplingMap);

        typeMetrics = SemanticEnricher.Enrich(
            typeMetrics, allTypes, allSyntaxTrees, compilationResult, thresholds, disabledRuleKinds);
        log.PhaseCompleted("semantic", sw.Elapsed);
        sw.Restart();

        log.PhaseStarted("aggregate");
        // Phase 1/2: WMC, NOC, TypeRank
        var nocMap = NocCalculator.Calculate(deps);
        var typeRankMap = RankCalculator.CalculateTypeRank(deps, allTypes);
        typeMetrics = EnrichWithNewMetrics(typeMetrics, nocMap, typeRankMap);

        var aggregationIndex = AssemblyAggregationIndex.Build(allTypes, deps);

        var assemblyInfos = targets.Select(a =>
        {
            var types = aggregationIndex.TypesByAssembly.TryGetValue(a.Name, out var asmTypes)
                ? asmTypes
                : [];
            var internalRelationCount = aggregationIndex.InternalRelationCounts.GetValueOrDefault(a.Name);
            var metrics = AssemblyMetrics.Compute(
                a.Name, types, couplingMap: couplingMap, internalRelationCount: internalRelationCount);
            var asmTypeMetrics = typeMetrics.Where(m => m.Assembly == a.Name).ToList();
            var health = CodeHealthCalculator.ComputeAssemblyHealth(asmTypeMetrics);
            return new AssemblyInfo(a.Name, a.Directory, a.References, metrics, health);
        }).ToList();

        IReadOnlyList<CyclicDependency>? cycles = null;
        if (!disableCycles)
        {
            var detectedCycles = CycleDetector.DetectAll(deps, assemblyInfos);
            cycles = detectedCycles.Count > 0 ? detectedCycles : null;
        }
        log.PhaseCompleted("aggregate", sw.Elapsed);

        return new AnalysisResult(
            Path.GetFullPath(path),
            DateTimeOffset.UtcNow,
            assemblyInfos,
            allTypes,
            deps,
            typeMetrics,
            analysisLevel,
            cycles,
            AnalysisResult.CurrentMetricsVersion,
            ToolVersionInfo.Current,
            ProjectKind: projectKind);
    }

    static CsprojInfo? ResolveCsprojInfo(
        string projectRoot, IReadOnlyList<string>? excludeDirectories, IAnalysisLogSink log)
    {
        var csprojFiles = CsprojParser.DiscoverCsprojFiles(projectRoot, excludeDirectories);
        if (csprojFiles.Count == 0) return null;

        var allRefs = new List<string>();
        var allDefines = new List<string>();
        string? langVersion = null;
        foreach (var csproj in csprojFiles)
        {
            var info = CsprojParser.TryParse(csproj);
            if (info is null) continue;
            allRefs.AddRange(info.ReferencePaths);
            allDefines.AddRange(info.DefineConstants);
            langVersion ??= info.LangVersion;
        }

        if (allRefs.Count == 0 && allDefines.Count == 0) return null;

        log.Info($"Found {csprojFiles.Count} .csproj file(s), {allRefs.Count} references, {allDefines.Count} defines");
        return new CsprojInfo(
            allRefs.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            [],
            allDefines.Distinct().ToList(),
            langVersion);
    }

    static IReadOnlyList<string> MergePreprocessorSymbols(string projectRoot, CsprojInfo? csprojInfo)
    {
        var symbols = UnityDllResolver.GetPreprocessorDefines(projectRoot);
        if (csprojInfo is not { DefineConstants.Count: > 0 }) return symbols;

        var merged = new List<string>(symbols);
        merged.AddRange(csprojInfo.DefineConstants);
        return merged.Distinct().ToList();
    }

    static (List<TypeNodeInfo> Types, List<SyntaxTree> Trees) CollectTypes(
        IReadOnlyList<AsmdefInfo> targets, IReadOnlyList<string> preprocessorSymbols,
        IReadOnlyList<string>? additionalExclude = null, bool excludeGeneratedCode = true,
        bool applyAnyDepthExcludes = true)
    {
        var allTypes = new List<TypeNodeInfo>();
        var allTrees = new List<SyntaxTree>();
        foreach (var asm in targets)
        {
            var merged = MergeExcludeDirectories(asm.ExcludeDirectories, additionalExclude);
            var result = TypeAnalyzer.AnalyzeDirectoryWithTrees(
                asm.Directory, asm.Name, preprocessorSymbols, merged, excludeGeneratedCode, applyAnyDepthExcludes);
            allTypes.AddRange(result.Types);
            allTrees.AddRange(result.SyntaxTrees);
        }
        return (allTypes, allTrees);
    }

    static IReadOnlyList<string>? MergeExcludeDirectories(
        IReadOnlyList<string>? asmExclude, IReadOnlyList<string>? configExclude)
    {
        if (asmExclude is not { Count: > 0 } && configExclude is not { Count: > 0 })
            return null;
        if (asmExclude is not { Count: > 0 })
            return configExclude;
        if (configExclude is not { Count: > 0 })
            return asmExclude;

        var merged = new List<string>(asmExclude.Count + configExclude.Count);
        merged.AddRange(asmExclude);
        merged.AddRange(configExclude);
        return merged;
    }

    static IReadOnlyList<TypeMetrics> EnrichWithNewMetrics(
        IReadOnlyList<TypeMetrics> typeMetrics,
        IReadOnlyDictionary<string, int> nocMap,
        IReadOnlyDictionary<string, double> typeRankMap)
    {
        var enriched = new List<TypeMetrics>(typeMetrics.Count);
        foreach (var metrics in typeMetrics)
        {
            var typeId = TypeIdentity.GetTypeId(metrics);
            nocMap.TryGetValue(typeId, out var noc);
            typeRankMap.TryGetValue(typeId, out var rank);
            enriched.Add(metrics with
            {
                Noc = noc,
                TypeRank = rank > 0 ? Math.Round(rank, 6) : null
            });
        }
        return enriched;
    }

    static IReadOnlyList<TypeMetrics> EnrichWithCouplingMetrics(
        IReadOnlyList<TypeMetrics> typeMetrics,
        IReadOnlyDictionary<string, CouplingInfo> couplingMap)
    {
        var enriched = new List<TypeMetrics>(typeMetrics.Count);
        foreach (var metrics in typeMetrics)
        {
            if (couplingMap.TryGetValue(TypeIdentity.GetTypeId(metrics), out var coupling))
            {
                enriched.Add(metrics with
                {
                    AfferentCoupling = coupling.AfferentCoupling,
                    EfferentCoupling = coupling.EfferentCoupling,
                    Instability = coupling.Instability.HasValue ? Math.Round(coupling.Instability.Value, 2) : null
                });
            }
            else
            {
                enriched.Add(metrics);
            }
        }
        return enriched;
    }
}
