using Microsoft.CodeAnalysis;

namespace Unilyze;

internal static class SyntaxIncrementalSemanticPhase
{
    public static (
        List<TypeNodeInfo> Types,
        List<TypeDependency> Dependencies,
        List<TypeMetrics> TypeMetrics,
        IReadOnlyDictionary<string, CouplingInfo> CouplingMap)
        Run(
            List<TypeNodeInfo> allTypes,
            List<SyntaxTree> allSyntaxTrees,
            CompilationResult compilationResult,
            AnalysisBuildOptions options,
            SyntaxIncrementalCollectResult collect)
    {
        allTypes = BaseTypeResolver.ResolveTypeRelationships(allTypes, allSyntaxTrees, compilationResult).ToList();

        var deps = DependencyBuilder.Build(allTypes).ToList();
        AppendDiRegistrationDependencies(deps, allSyntaxTrees, compilationResult, allTypes);

        var baseMetrics = CodeHealthCalculator.ComputeTypeMetrics(allTypes);
        var couplingMap = CouplingMetricsCalculator.Calculate(deps, allTypes);

        var config = options.EffectiveAnalysisConfig;
        var typesToReEnrich = DetermineTypesToReEnrich(allTypes, collect);
        var metricsByTypeId = new Dictionary<string, TypeMetrics>(StringComparer.Ordinal);

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
                metricsByTypeId[typeId] = cached.Metrics;
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

        var finalMetrics = new List<TypeMetrics>(baseMetrics.Count);
        foreach (var metrics in baseMetrics)
        {
            var typeId = TypeIdentity.GetTypeId(metrics);
            var enriched = metricsByTypeId[typeId];
            enriched = SyntaxCacheMetrics.ApplyCouplingFields(enriched, couplingMap);
            finalMetrics.Add(enriched);
        }

        allTypes = TypeRoleStamper.ApplyRoles(allTypes, allSyntaxTrees, compilationResult).ToList();
        return (allTypes, deps, finalMetrics, couplingMap);
    }

    static HashSet<string> DetermineTypesToReEnrich(
        IReadOnlyList<TypeNodeInfo> allTypes,
        SyntaxIncrementalCollectResult collect)
    {
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
