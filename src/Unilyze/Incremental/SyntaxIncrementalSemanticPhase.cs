using Unilyze.Config;
using Unilyze.DI;
using Unilyze.Metrics;
using Unilyze.Pipeline;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace Unilyze.Incremental;

internal static class SyntaxIncrementalSemanticPhase
{
    public static (
        List<TypeNodeInfo> Types,
        List<TypeDependency> Dependencies,
        List<TypeMetrics> TypeMetrics,
        IReadOnlyDictionary<string, CouplingInfo> CouplingMap,
        IReadOnlyDictionary<string, IReadOnlyList<string>> UsedTypesByTypeId)
        Run(
            List<TypeNodeInfo> allTypes,
            List<SyntaxTree> allSyntaxTrees,
            CompilationResult compilationResult,
            AnalysisBuildOptions options,
            SyntaxIncrementalCollectResult collect,
            PipelineDiscoverState discover)
    {
        allTypes = BaseTypeResolver.ResolveTypeRelationships(allTypes, allSyntaxTrees, compilationResult).ToList();

        var deps = DependencyBuilder.Build(allTypes).ToList();
        AppendDiRegistrationDependencies(deps, allSyntaxTrees, compilationResult, allTypes);
        // Keep incremental runs metric-identical to full runs: scene/prefab
        // SerializedReference edges (#132) must be appended on this path too.
        AnalysisPipelineSemanticPhase.AppendSerializedReferenceDependencies(deps, discover, allTypes, options);

        var baseMetrics = CodeHealthCalculator.ComputeTypeMetrics(allTypes);
        var couplingMap = CouplingMetricsCalculator.Calculate(deps, allTypes);

        var config = options.EffectiveAnalysisConfig;
        var typesToReEnrich = DetermineTypesToReEnrich(allTypes, collect);
        options.EffectiveLog.Info(
            $"[incremental] re-enrich types: {typesToReEnrich.Count}/{allTypes.Count}");
        var metricsByTypeId = new Dictionary<string, TypeMetrics>(StringComparer.Ordinal);
        var usedTypesByTypeId = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        var reEnrichBaseMetrics = new List<TypeMetrics>();
        for (var i = 0; i < baseMetrics.Count; i++)
        {
            var metrics = baseMetrics[i];
            var typeId = TypeIdentity.GetTypeId(metrics);
            if (typesToReEnrich.Contains(typeId))
            {
                reEnrichBaseMetrics.Add(metrics);
                continue;
            }

            if (collect.CachedEnrichmentByTypeId.TryGetValue(typeId, out var cached))
            {
                metricsByTypeId[typeId] = cached.Metrics;
                usedTypesByTypeId[typeId] = cached.UsedTypes;
            }
            else
                reEnrichBaseMetrics.Add(metrics);
        }

        if (reEnrichBaseMetrics.Count > 0)
        {
            var enriched = SemanticEnricher.Enrich(
                reEnrichBaseMetrics, allTypes, allSyntaxTrees, compilationResult,
                config.Profile, config.SmellOverrides, config.InformationalSmellKinds,
                config.DisabledRuleKinds, options.EffectiveMaxParallelism);
            foreach (var metrics in enriched)
                metricsByTypeId[TypeIdentity.GetTypeId(metrics)] = SyntaxCacheMetrics.StripCouplingFields(metrics);
        }

        var reEnrichTypeIds = new HashSet<string>(
            reEnrichBaseMetrics.Select(TypeIdentity.GetTypeId), StringComparer.Ordinal);
        var recordedUsedTypes = RecordUsedTypes(
            allTypes, allSyntaxTrees, compilationResult, collect, reEnrichTypeIds, options);
        foreach (var (typeId, usedTypes) in recordedUsedTypes)
            usedTypesByTypeId[typeId] = usedTypes;

        var finalMetrics = new List<TypeMetrics>(baseMetrics.Count);
        foreach (var metrics in baseMetrics)
        {
            var typeId = TypeIdentity.GetTypeId(metrics);
            var enriched = metricsByTypeId[typeId];
            enriched = SyntaxCacheMetrics.ApplyCouplingFields(enriched, couplingMap);
            finalMetrics.Add(enriched);
        }

        allTypes = TypeRoleStamper.ApplyRoles(allTypes, allSyntaxTrees, compilationResult).ToList();
        return (allTypes, deps, finalMetrics, couplingMap, usedTypesByTypeId);
    }

    // UsageRecorder pass (design doc §4.1-4.2): runs only for types being re-enriched anyway
    // (they already pay per-node SemanticModel walks in CBO/RFC/boxing/closure), one dedicated
    // IOperation walk per type. Cache-hit types carry over their manifest UsedTypes above instead
    // of re-recording. Record-only — nothing downstream reads UsedTypesByTypeId yet.
    static IReadOnlyDictionary<string, IReadOnlyList<string>> RecordUsedTypes(
        IReadOnlyList<TypeNodeInfo> allTypes,
        IReadOnlyList<SyntaxTree> allSyntaxTrees,
        CompilationResult compilationResult,
        SyntaxIncrementalCollectResult collect,
        IReadOnlySet<string> reEnrichTypeIds,
        AnalysisBuildOptions options)
    {
        if (compilationResult.Compilation is null)
            return new Dictionary<string, IReadOnlyList<string>>();

        var treeByPath = SyntaxLookups.BuildTreeLookup(compilationResult, allSyntaxTrees);
        var typeDeclLookup = SyntaxLookups.BuildTypeDeclLookup(allTypes, treeByPath);
        var assemblyByFilePath = BuildAssemblyByFilePath(collect.RawTypesByFile);

        var targets = reEnrichTypeIds
            .Where(typeDeclLookup.ContainsKey)
            .Select(typeId => (TypeId: typeId, Decl: typeDeclLookup[typeId]))
            .ToList();

        var modelCache = new ConcurrentDictionary<SyntaxTree, SemanticModel>();
        var results = new ConcurrentDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = UnilyzeConfig.ResolveMaxParallelism(options.EffectiveMaxParallelism)
        };
        Parallel.ForEach(targets, parallelOptions, target =>
        {
            var model = modelCache.GetOrAdd(
                target.Decl.SyntaxTree,
                static (t, c) => c.GetSemanticModel(t),
                compilationResult.Compilation);
            results[target.TypeId] = UsageRecorder.Record(target.Decl, model, assemblyByFilePath);
        });

        return results;
    }

    static Dictionary<string, string> BuildAssemblyByFilePath(
        IReadOnlyDictionary<string, IReadOnlyList<TypeNodeInfo>> rawTypesByFile) =>
        rawTypesByFile.ToDictionary(
            kvp => Path.GetFullPath(kvp.Key),
            kvp => kvp.Value.FirstOrDefault()?.Assembly ?? "Assembly-CSharp",
            StringComparer.Ordinal);

    static HashSet<string> DetermineTypesToReEnrich(
        IReadOnlyList<TypeNodeInfo> allTypes,
        SyntaxIncrementalCollectResult collect)
    {
        // A structural change (signature/type-set/global-using/file add or delete) can shift
        // name resolution for body-callers the declaration dependency graph never captures, so
        // the only correctness-safe answer is to re-enrich every type.
        if (collect.RequiresFullReEnrich)
            return new HashSet<string>(allTypes.Select(TypeIdentity.GetTypeId), StringComparer.Ordinal);

        var reparsedFiles = new HashSet<string>(
            collect.ReparsedFiles.Select(Path.GetFullPath),
            StringComparer.Ordinal);

        var typesToReEnrich = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in allTypes)
        {
            var typeId = TypeIdentity.GetTypeId(type);
            if (reparsedFiles.Contains(Path.GetFullPath(type.FilePath)))
            {
                typesToReEnrich.Add(typeId);
                continue;
            }

            if (type.Modifiers.Contains("partial"))
            {
                var partFiles = collect.RawTypesByFile
                    .Where(kvp => kvp.Value.Any(t => TypeIdentity.GetTypeId(t) == typeId))
                    .Select(kvp => Path.GetFullPath(kvp.Key));
                if (partFiles.Any(reparsedFiles.Contains))
                    typesToReEnrich.Add(typeId);
            }
        }

        return typesToReEnrich;
    }

    static void AppendDiRegistrationDependencies(
        List<TypeDependency> deps,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        CompilationResult compilationResult,
        IReadOnlyList<TypeNodeInfo> allTypes)
    {
        var diRegistrations = DIContainerAnalyzer.Analyze(syntaxTrees, compilationResult.Compilation);
        var diTypeIndex = DITypeIdIndex.Build(allTypes);
        foreach (var reg in diRegistrations)
        {
            var fromTypeId = diTypeIndex.Resolve(reg.ServiceType, reg.ServiceTypeQualified);
            var toTypeId = diTypeIndex.Resolve(reg.ImplementationType, reg.ImplementationTypeQualified);
            deps.Add(new TypeDependency(
                reg.ServiceType, reg.ImplementationType, DependencyKind.DIRegistration, fromTypeId, toTypeId));
        }
    }
}
