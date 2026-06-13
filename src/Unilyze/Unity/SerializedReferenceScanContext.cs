using Unilyze.Pipeline;
namespace Unilyze.Unity;

internal sealed class SerializedReferenceScanContext
{
    readonly Dictionary<string, UnityAssetParseResult> _parseCache = new(StringComparer.OrdinalIgnoreCase);

    public SerializedReferenceScanContext(
        UnityAssetGuidIndex guidIndex,
        IReadOnlyDictionary<string, HashSet<string>> serializedFields,
        IReadOnlyDictionary<string, TypeNodeInfo> typeById)
    {
        GuidIndex = guidIndex;
        SerializedFields = serializedFields;
        TypeById = typeById;
    }

    public UnityAssetGuidIndex GuidIndex { get; }
    public IReadOnlyDictionary<string, HashSet<string>> SerializedFields { get; }
    public IReadOnlyDictionary<string, TypeNodeInfo> TypeById { get; }

    public bool TryGetParsedAsset(string assetFile, out UnityAssetParseResult parsed)
    {
        if (_parseCache.TryGetValue(assetFile, out parsed!))
            return true;

        var parsedResult = UnitySceneReferenceParser.TryParseFile(assetFile);
        if (parsedResult is null)
            return false;

        _parseCache[assetFile] = parsedResult;
        parsed = parsedResult;
        return true;
    }
}
