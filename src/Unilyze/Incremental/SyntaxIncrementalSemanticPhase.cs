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

        collect = ResolvePreciseMembersAndBase(collect, deps);

        var config = options.EffectiveAnalysisConfig;
        var (typesToReEnrich, reEnrichLogSuffix) = DetermineTypesToReEnrich(allTypes, collect, options.EffectiveLog);
        var logSuffix = reEnrichLogSuffix is null ? "" : $" {reEnrichLogSuffix}";
        options.EffectiveLog.Info(
            $"[incremental] re-enrich types: {typesToReEnrich.Count}/{allTypes.Count}{logSuffix}");
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

    // Phase B (design doc §4.3, §6): resolves the raw Δmembers(B)/Δbase(B) classification the
    // collector returned into the actual invalidation TypeIds, using InhDesc(B) — the transitive
    // inheritance/interface-implementation descendant closure — built from THIS generation's
    // declaration graph (`deps`, just rebuilt full above). This is the one place that closure is
    // available: the collector runs before `deps` exists, so it can only classify WHICH types
    // changed, not resolve who is affected.
    //   Δmembers(B) -> RDeps(B ∪ InhDesc(B))
    //   Δbase(B)    -> InhDesc(B) ∪ RDeps(B ∪ InhDesc(B))
    // Both union into the already-Δsig/Δusing-resolved PreciseExtraReEnrichTypeIds the collector
    // returned; PreciseLogSuffix already carries the correct sig/members/base/using counts (they
    // only need the classified TypeId COUNT, not the resolved closure), so it is left untouched.
    // No-op (returns `collect` unchanged) when there is nothing to resolve: a full-fallback
    // generation, a body-only generation, or a cold/no-cache generation all leave
    // MembersChangedTypeIds/BaseChangedTypeIds empty.
    //
    // internal (not private), matching DetermineTypesToReEnrich below, so the Δmembers/Δbase +
    // InhDesc resolution has direct unit-test coverage without spinning up a real CLI analysis.
    internal static SyntaxIncrementalCollectResult ResolvePreciseMembersAndBase(
        SyntaxIncrementalCollectResult collect, IReadOnlyList<TypeDependency> deps)
    {
        if (collect.RequiresFullReEnrich)
            return collect;

        var membersChanged = collect.MembersChangedTypeIds;
        var baseChanged = collect.BaseChangedTypeIds;
        if (membersChanged is not { Count: > 0 } && baseChanged is not { Count: > 0 })
            return collect;

        var rdeps = collect.Rdeps ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var childrenByParent = InheritanceDescendantIndex.Build(deps);
        var extra = new HashSet<string>(StringComparer.Ordinal);
        if (collect.PreciseExtraReEnrichTypeIds is { } existingExtra)
            extra.UnionWith(existingExtra);

        foreach (var b in membersChanged ?? [])
        {
            var closure = InheritanceDescendantIndex.DescendantsOf(childrenByParent, b);
            extra.UnionWith(ReverseDependencyIndex.ResolveMany(rdeps, closure.Append(b)));
        }

        foreach (var b in baseChanged ?? [])
        {
            var closure = InheritanceDescendantIndex.DescendantsOf(childrenByParent, b);
            extra.UnionWith(closure); // InhDesc(B) itself: DIT/inherited-binding may have shifted
            extra.UnionWith(ReverseDependencyIndex.ResolveMany(rdeps, closure.Append(b)));
        }

        return collect with { PreciseExtraReEnrichTypeIds = extra };
    }

    // Fraction of all types above which the precise (RDI) invalidation set collapses to a full
    // re-enrich: bookkeeping a near-total re-enrich set is pure overhead once it stops being a
    // meaningful elision (design doc §4.3). Tunable — revisit if a corpus shows the collapse
    // firing on edits that should stay precise, or never firing when it should.
    internal const double CollapseThresholdRatio = 0.6;

    // internal (not private) so the collapse threshold has direct unit-test coverage without
    // spinning up a real CLI analysis (SyntaxIncrementalSemanticPhaseTests).
    internal static (HashSet<string> TypeIds, string? LogSuffix) DetermineTypesToReEnrich(
        IReadOnlyList<TypeNodeInfo> allTypes,
        SyntaxIncrementalCollectResult collect,
        IAnalysisLogSink log)
    {
        // A structural change with no precise rule yet (type-set/global-using/file add or
        // delete, member add/remove, base/interface change) can shift name resolution for
        // body-callers the declaration dependency graph never captures, so the only
        // correctness-safe answer is to re-enrich every type. Δsig(B)/Δusing(F) are handled
        // below via PreciseExtraReEnrichTypeIds instead of setting this flag.
        if (collect.RequiresFullReEnrich)
            return (new HashSet<string>(allTypes.Select(TypeIdentity.GetTypeId), StringComparer.Ordinal), null);

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

        if (collect.PreciseExtraReEnrichTypeIds is { Count: > 0 } extra)
            typesToReEnrich.UnionWith(extra);

        // The collapse only applies when the precise (RDI) path was engaged this generation
        // (PreciseLogSuffix is set exactly when Δsig/Δusing deltas were classified): v1 handled
        // those generations with a full re-enrich, so collapsing is never worse than v1. A pure
        // body-only bulk edit (no deltas) must NEVER collapse — SEED-only is v1's proven fast
        // path, and collapsing it would regress large structurally-clean edits.
        if (collect.PreciseLogSuffix is not null
            && allTypes.Count > 0
            && typesToReEnrich.Count > CollapseThresholdRatio * allTypes.Count)
        {
            log.Info(
                $"[incremental] full re-enrich: precise invalidation set {typesToReEnrich.Count}/{allTypes.Count} "
                + $"exceeds the {CollapseThresholdRatio:P0} collapse threshold");
            return (new HashSet<string>(allTypes.Select(TypeIdentity.GetTypeId), StringComparer.Ordinal), null);
        }

        return (typesToReEnrich, collect.PreciseLogSuffix);
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
