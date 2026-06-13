using Unilyze.Pipeline;
namespace Unilyze.Unity;

internal static class SerializedReferenceResolver
{
    public static IReadOnlyList<TypeDependency> Resolve(
        string assetsDir,
        IReadOnlyList<TypeNodeInfo> types,
        IReadOnlyList<string>? excludeDirectories,
        bool excludeGeneratedCode,
        bool applyAnyDepthExcludes)
    {
        var guidIndex = UnityAssetGuidIndex.Build(
            assetsDir, types, excludeDirectories, excludeGeneratedCode, applyAnyDepthExcludes);
        var serializedFields = SerializedReferenceFieldIndex.Build(types);
        if (serializedFields.Count == 0)
            return [];

        var context = new SerializedReferenceScanContext(
            guidIndex,
            serializedFields,
            types.ToDictionary(TypeIdentity.GetTypeId, t => t, StringComparer.Ordinal));
        var edges = new HashSet<(string FromTypeId, string ToTypeId)>();

        foreach (var assetFile in UnitySerializedAssetEnumerator.Enumerate(
                     assetsDir, excludeDirectories, applyAnyDepthExcludes))
        {
            if (!context.TryGetParsedAsset(assetFile, out var parsed))
                continue;
            SerializedReferenceEdgeCollector.CollectFromAsset(parsed, context, edges);
        }

        return MaterializeDependencies(edges, context.TypeById);
    }

    static List<TypeDependency> MaterializeDependencies(
        HashSet<(string FromTypeId, string ToTypeId)> edges,
        IReadOnlyDictionary<string, TypeNodeInfo> typeById)
    {
        var deps = new List<TypeDependency>(edges.Count);
        foreach (var (fromTypeId, toTypeId) in edges)
        {
            if (!typeById.TryGetValue(fromTypeId, out var fromType)
                || !typeById.TryGetValue(toTypeId, out var toType))
                continue;

            deps.Add(new TypeDependency(
                fromType.Name,
                toType.Name,
                DependencyKind.SerializedReference,
                fromTypeId,
                toTypeId));
        }

        return deps;
    }
}
