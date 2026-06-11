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
            AnalysisBuildOptions options)
    {
        allTypes = BaseTypeResolver.ResolveTypeRelationships(allTypes, allSyntaxTrees, compilationResult).ToList();

        var deps = DependencyBuilder.Build(allTypes).ToList();
        AppendDiRegistrationDependencies(deps, allSyntaxTrees, compilationResult, allTypes);

        var typeMetrics = CodeHealthCalculator.ComputeTypeMetrics(allTypes);
        var couplingMap = CouplingMetricsCalculator.Calculate(deps, allTypes);
        typeMetrics = EnrichWithCouplingMetrics(typeMetrics, couplingMap);

        var config = options.EffectiveAnalysisConfig;
        typeMetrics = SemanticEnricher.Enrich(
            typeMetrics, allTypes, allSyntaxTrees, compilationResult,
            config.Profile, config.SmellOverrides, config.InformationalSmellKinds,
            config.DisabledRuleKinds);
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
