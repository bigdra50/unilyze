namespace Unilyze;

public sealed record AssemblyMetrics(
    string AssemblyName,
    int TypeCount,
    int ClassCount,
    int RecordCount,
    int InterfaceCount,
    int EnumCount,
    int DelegateCount,
    int PublicTypeCount,
    int SealedTypeCount,
    int TotalMembers,
    IReadOnlyList<string> Namespaces,
    double Abstractness = 0.0,
    double? DistanceFromMainSequence = null,
    double? RelationalCohesion = null,
    double? BurstCoverage = null,
    int? EcsTypeCount = null)
{
    public static AssemblyMetrics Compute(
        string assemblyName,
        IReadOnlyList<TypeNodeInfo> types,
        IReadOnlyList<TypeDependency>? dependencies = null,
        IReadOnlyDictionary<string, CouplingInfo>? couplingMap = null,
        int? internalRelationCount = null)
    {
        var abstractness = ComputeAbstractness(types);
        var instability = ComputeAssemblyInstability(assemblyName, types, couplingMap);
        var dfms = instability.HasValue ? Math.Abs(abstractness + instability.Value - 1.0) : (double?)null;
        var relCohesion = internalRelationCount.HasValue || dependencies is not null
            ? ComputeRelationalCohesion(types, internalRelationCount, dependencies)
            : null;
        var (burstCoverage, ecsTypeCount) = ComputeEcsMetrics(types);

        return new AssemblyMetrics(
            AssemblyName: assemblyName,
            TypeCount: types.Count,
            ClassCount: types.Count(t => t.Kind == "class"),
            RecordCount: types.Count(t => t.Kind is "record" or "record struct"),
            InterfaceCount: types.Count(t => t.Kind == "interface"),
            EnumCount: types.Count(t => t.Kind == "enum"),
            DelegateCount: types.Count(t => t.Kind == "delegate"),
            PublicTypeCount: types.Count(t => t.Modifiers.Contains("public")),
            SealedTypeCount: types.Count(t => t.Modifiers.Contains("sealed")),
            TotalMembers: types.Sum(t => t.Members.Count),
            Namespaces: types.Select(t => t.Namespace).Where(n => n.Length > 0).Distinct().Order().ToList(),
            Abstractness: abstractness,
            DistanceFromMainSequence: dfms.HasValue ? Math.Round(dfms.Value, 4) : null,
            RelationalCohesion: relCohesion.HasValue ? Math.Round(relCohesion.Value, 4) : null,
            BurstCoverage: burstCoverage,
            EcsTypeCount: ecsTypeCount);
    }

    static (double? BurstCoverage, int? EcsTypeCount) ComputeEcsMetrics(IReadOnlyList<TypeNodeInfo> types)
    {
        var ecsTypes = types.Where(t => t.Role is TypeRole.EcsSystem or TypeRole.EcsJob or TypeRole.EcsComponentData).ToList();
        if (ecsTypes.Count == 0)
            return (null, null);

        var eligible = ecsTypes.Where(t => EcsContextClassifier.IsBurstEligible(t.Role!.Value, t.Kind)).ToList();
        double? burstCoverage = null;
        if (eligible.Count > 0)
        {
            var covered = eligible.Count(EcsBurstCompileChecker.IsBurstCovered);
            burstCoverage = Math.Round((double)covered / eligible.Count, 4);
        }

        return (burstCoverage, ecsTypes.Count);
    }

    static double ComputeAbstractness(IReadOnlyList<TypeNodeInfo> types)
    {
        if (types.Count == 0) return 0.0;

        var abstractCount = types.Count(t =>
            (t.Kind == "class" && t.Modifiers.Contains("abstract")) ||
            t.Kind == "interface");

        return (double)abstractCount / types.Count;
    }

    static double? ComputeAssemblyInstability(
        string assemblyName,
        IReadOnlyList<TypeNodeInfo> types,
        IReadOnlyDictionary<string, CouplingInfo>? couplingMap)
    {
        if (couplingMap is null) return null;

        long totalCa = 0;
        long totalCe = 0;
        foreach (var type in types)
        {
            var typeId = TypeIdentity.GetTypeId(type);
            if (couplingMap.TryGetValue(typeId, out var coupling))
            {
                totalCa += coupling.AfferentCoupling;
                totalCe += coupling.EfferentCoupling;
            }
        }

        var total = totalCa + totalCe;
        return total > 0 ? (double)totalCe / total : 0.0;
    }

    static double? ComputeRelationalCohesion(
        IReadOnlyList<TypeNodeInfo> types,
        int? internalRelationCount,
        IReadOnlyList<TypeDependency>? dependencies)
    {
        var n = types.Count;
        if (n <= 1) return null;

        var r = internalRelationCount ?? CountInternalRelations(types, dependencies!);
        return (double)(r + 1) / n;
    }

    static int CountInternalRelations(
        IReadOnlyList<TypeNodeInfo> types,
        IReadOnlyList<TypeDependency> dependencies)
    {
        var assemblyTypeIds = new HashSet<string>(types.Select(TypeIdentity.GetTypeId));

        var seen = new HashSet<(string, string)>();
        var r = 0;
        foreach (var dep in dependencies)
        {
            if (dep.FromTypeId is null || dep.ToTypeId is null) continue;
            if (dep.FromTypeId == dep.ToTypeId) continue;
            if (!assemblyTypeIds.Contains(dep.FromTypeId) || !assemblyTypeIds.Contains(dep.ToTypeId)) continue;
            if (seen.Add((dep.FromTypeId, dep.ToTypeId)))
                r++;
        }

        return r;
    }
}
