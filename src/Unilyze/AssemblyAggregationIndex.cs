namespace Unilyze;

internal sealed class AssemblyAggregationIndex
{
    public IReadOnlyDictionary<string, IReadOnlyList<TypeNodeInfo>> TypesByAssembly { get; }
    public IReadOnlyDictionary<string, string> TypeIdToAssembly { get; }
    public IReadOnlyDictionary<string, int> InternalRelationCounts { get; }

    AssemblyAggregationIndex(
        IReadOnlyDictionary<string, IReadOnlyList<TypeNodeInfo>> typesByAssembly,
        IReadOnlyDictionary<string, string> typeIdToAssembly,
        IReadOnlyDictionary<string, int> internalRelationCounts)
    {
        TypesByAssembly = typesByAssembly;
        TypeIdToAssembly = typeIdToAssembly;
        InternalRelationCounts = internalRelationCounts;
    }

    public static AssemblyAggregationIndex Build(
        IReadOnlyList<TypeNodeInfo> allTypes,
        IReadOnlyList<TypeDependency> dependencies)
    {
        var (typesByAssembly, typeIdToAssembly) = MapTypesToAssemblies(allTypes);
        var internalRelationCounts = CountInternalRelations(dependencies, typeIdToAssembly);

        var readonlyTypesByAssembly = typesByAssembly.ToDictionary(
            static kv => kv.Key,
            static kv => (IReadOnlyList<TypeNodeInfo>)kv.Value);

        return new AssemblyAggregationIndex(readonlyTypesByAssembly, typeIdToAssembly, internalRelationCounts);
    }

    static (Dictionary<string, List<TypeNodeInfo>> TypesByAssembly, Dictionary<string, string> TypeIdToAssembly)
        MapTypesToAssemblies(IReadOnlyList<TypeNodeInfo> allTypes)
    {
        var typesByAssembly = new Dictionary<string, List<TypeNodeInfo>>();
        var typeIdToAssembly = new Dictionary<string, string>(allTypes.Count);

        foreach (var type in allTypes)
        {
            var typeId = TypeIdentity.GetTypeId(type);
            typeIdToAssembly[typeId] = type.Assembly;
            if (!typesByAssembly.TryGetValue(type.Assembly, out var list))
            {
                list = [];
                typesByAssembly[type.Assembly] = list;
            }

            list.Add(type);
        }

        return (typesByAssembly, typeIdToAssembly);
    }

    static Dictionary<string, int> CountInternalRelations(
        IReadOnlyList<TypeDependency> dependencies,
        Dictionary<string, string> typeIdToAssembly)
    {
        var internalRelationCounts = new Dictionary<string, int>();
        var seenByAssembly = new Dictionary<string, HashSet<(string From, string To)>>();

        foreach (var dep in dependencies)
        {
            if (dep.FromTypeId is null || dep.ToTypeId is null)
                continue;
            if (dep.FromTypeId == dep.ToTypeId)
                continue;
            if (!typeIdToAssembly.TryGetValue(dep.FromTypeId, out var fromAssembly))
                continue;
            if (!typeIdToAssembly.TryGetValue(dep.ToTypeId, out var toAssembly))
                continue;
            if (fromAssembly != toAssembly)
                continue;

            if (!seenByAssembly.TryGetValue(fromAssembly, out var seen))
            {
                seen = [];
                seenByAssembly[fromAssembly] = seen;
            }

            if (seen.Add((dep.FromTypeId, dep.ToTypeId)))
                internalRelationCounts[fromAssembly] = internalRelationCounts.GetValueOrDefault(fromAssembly) + 1;
        }

        return internalRelationCounts;
    }
}
