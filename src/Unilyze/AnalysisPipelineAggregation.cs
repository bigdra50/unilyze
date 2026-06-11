namespace Unilyze;

internal static class AnalysisPipelineAggregation
{
    public static (IReadOnlyList<TypeMetrics> TypeMetrics, List<AssemblyInfo> AssemblyInfos, IReadOnlyList<CyclicDependency>? Cycles)
        Run(
            PipelineDiscoverState discover,
            IReadOnlyList<TypeNodeInfo> allTypes,
            IReadOnlyList<TypeDependency> deps,
            IReadOnlyList<TypeMetrics> typeMetrics,
            IReadOnlyDictionary<string, CouplingInfo> couplingMap,
            bool disableCycles)
    {
        var nocMap = NocCalculator.Calculate(deps);
        var typeRankMap = RankCalculator.CalculateTypeRank(deps, allTypes);
        typeMetrics = EnrichWithNewMetrics(typeMetrics, nocMap, typeRankMap);

        var aggregationIndex = AssemblyAggregationIndex.Build(allTypes, deps);
        var assemblyInfos = BuildAssemblyInfos(discover.Targets, aggregationIndex, couplingMap, typeMetrics);

        IReadOnlyList<CyclicDependency>? cycles = null;
        if (!disableCycles)
        {
            var detectedCycles = CycleDetector.DetectAll(deps, assemblyInfos);
            cycles = detectedCycles.Count > 0 ? detectedCycles : null;
        }

        return (typeMetrics, assemblyInfos, cycles);
    }

    static List<AssemblyInfo> BuildAssemblyInfos(
        IReadOnlyList<AsmdefInfo> targets,
        AssemblyAggregationIndex aggregationIndex,
        IReadOnlyDictionary<string, CouplingInfo> couplingMap,
        IReadOnlyList<TypeMetrics> typeMetrics)
    {
        var assemblyInfos = new List<AssemblyInfo>(targets.Count);
        foreach (var asm in targets)
        {
            var types = aggregationIndex.TypesByAssembly.TryGetValue(asm.Name, out var asmTypes)
                ? asmTypes
                : [];
            var internalRelationCount = aggregationIndex.InternalRelationCounts.GetValueOrDefault(asm.Name);
            var metrics = AssemblyMetrics.Compute(
                asm.Name, types, couplingMap: couplingMap, internalRelationCount: internalRelationCount);
            var asmTypeMetrics = FilterMetricsByAssembly(typeMetrics, asm.Name);
            var health = CodeHealthCalculator.ComputeAssemblyHealth(asmTypeMetrics);
            assemblyInfos.Add(new AssemblyInfo(asm.Name, asm.Directory, asm.References, metrics, health));
        }

        return assemblyInfos;
    }

    static List<TypeMetrics> FilterMetricsByAssembly(IReadOnlyList<TypeMetrics> typeMetrics, string assemblyName)
    {
        var filtered = new List<TypeMetrics>();
        foreach (var metrics in typeMetrics)
        {
            if (metrics.Assembly == assemblyName)
                filtered.Add(metrics);
        }

        return filtered;
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
}
