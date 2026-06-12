namespace Unilyze;

internal sealed class UnityAssetGuidIndex
{
    readonly Dictionary<string, string> _guidToAssetPath;
    readonly Dictionary<string, string> _guidToTypeId;

    UnityAssetGuidIndex(
        Dictionary<string, string> guidToAssetPath,
        Dictionary<string, string> guidToTypeId)
    {
        _guidToAssetPath = guidToAssetPath;
        _guidToTypeId = guidToTypeId;
    }

    public static UnityAssetGuidIndex Build(
        string assetsDir,
        IReadOnlyList<TypeNodeInfo> types,
        IReadOnlyList<string>? excludeDirectories,
        bool excludeGeneratedCode,
        bool applyAnyDepthExcludes)
    {
        var guidToAssetPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var metaFile in Directory.EnumerateFiles(assetsDir, "*.meta", SearchOption.AllDirectories))
        {
            if (DefaultExcludes.ShouldExcludeSourceFile(
                    metaFile, excludeDirectories, excludeGeneratedCode: false, assetsDir, applyAnyDepthExcludes))
                continue;

            var guid = UnityMetaGuidReader.TryReadGuid(metaFile);
            if (string.IsNullOrWhiteSpace(guid))
                continue;

            var assetPath = Path.GetFullPath(metaFile[..^".meta".Length]);
            guidToAssetPath.TryAdd(guid, assetPath);
        }

        var pathToTypeId = types
            .GroupBy(t => Path.GetFullPath(t.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => TypeIdentity.GetTypeId(g.First()), StringComparer.OrdinalIgnoreCase);

        var guidToTypeId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (guid, assetPath) in guidToAssetPath)
        {
            if (!assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                continue;
            if (pathToTypeId.TryGetValue(assetPath, out var typeId))
                guidToTypeId.TryAdd(guid, typeId);
        }

        return new UnityAssetGuidIndex(guidToAssetPath, guidToTypeId);
    }

    public bool TryGetAssetPath(string guid, out string assetPath)
        => _guidToAssetPath.TryGetValue(guid, out assetPath!);

    public bool TryGetTypeId(string scriptGuid, out string typeId)
        => _guidToTypeId.TryGetValue(scriptGuid, out typeId!);
}
