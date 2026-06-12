using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze;

internal static class AnalysisPipelineSemanticPhase
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
            PipelineDiscoverState discover)
    {
        allTypes = BaseTypeResolver.ResolveTypeRelationships(allTypes, allSyntaxTrees, compilationResult).ToList();

        var deps = DependencyBuilder.Build(allTypes).ToList();
        AppendDiRegistrationDependencies(deps, allSyntaxTrees, compilationResult, allTypes);
        AppendSerializedReferenceDependencies(deps, discover, allTypes, options);

        var typeMetrics = CodeHealthCalculator.ComputeTypeMetrics(allTypes);
        var couplingMap = CouplingMetricsCalculator.Calculate(deps, allTypes);
        typeMetrics = EnrichWithCouplingMetrics(typeMetrics, couplingMap);

        var config = options.EffectiveAnalysisConfig;
        typeMetrics = SemanticEnricher.Enrich(
            typeMetrics, allTypes, allSyntaxTrees, compilationResult,
            config.Profile, config.SmellOverrides, config.InformationalSmellKinds,
            config.DisabledRuleKinds, options.EffectiveMaxParallelism);
        allTypes = TypeRoleStamper.ApplyRoles(allTypes, allSyntaxTrees, compilationResult).ToList();

        return (allTypes, deps, typeMetrics.ToList(), couplingMap);
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

    internal static void AppendSerializedReferenceDependencies(
        List<TypeDependency> deps,
        PipelineDiscoverState discover,
        IReadOnlyList<TypeNodeInfo> allTypes,
        AnalysisBuildOptions options)
    {
        if (discover.ProjectKind != "unity")
            return;

        var assetsDir = ProgramHelpers.ResolveAssetsDir(discover.ProjectRoot);
        var excludeDirectories = MergeSerializedReferenceExcludes(discover, options);
        var serializedDeps = SerializedReferenceResolver.Resolve(
            assetsDir,
            allTypes,
            excludeDirectories,
            options.ExcludeGeneratedCode,
            options.ApplyAnyDepthExcludes);
        deps.AddRange(serializedDeps);
    }

    static IReadOnlyList<string>? MergeSerializedReferenceExcludes(
        PipelineDiscoverState discover,
        AnalysisBuildOptions options)
    {
        var projectExcludes = DefaultExcludes.ResolveProjectPaths(discover.ProjectRoot);
        if (options.ExcludeDirectories is not { Count: > 0 })
            return projectExcludes;

        var merged = new List<string>(projectExcludes.Count + options.ExcludeDirectories.Count);
        merged.AddRange(projectExcludes);
        merged.AddRange(options.ExcludeDirectories);
        return merged;
    }

    static IReadOnlyList<TypeMetrics> EnrichWithCouplingMetrics(
        IReadOnlyList<TypeMetrics> typeMetrics,
        IReadOnlyDictionary<string, CouplingInfo> couplingMap)
    {
        var enriched = new List<TypeMetrics>(typeMetrics.Count);
        foreach (var metrics in typeMetrics)
        {
            if (!couplingMap.TryGetValue(TypeIdentity.GetTypeId(metrics), out var coupling))
            {
                enriched.Add(metrics);
                continue;
            }

            enriched.Add(metrics with
            {
                AfferentCoupling = coupling.AfferentCoupling,
                EfferentCoupling = coupling.EfferentCoupling,
                Instability = coupling.Instability.HasValue ? Math.Round(coupling.Instability.Value, 2) : null
            });
        }

        return enriched;
    }
}
