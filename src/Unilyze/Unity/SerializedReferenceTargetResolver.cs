namespace Unilyze.Unity;

internal static class SerializedReferenceTargetResolver
{
    public static bool TryResolveTypeId(
        UnityObjectReference reference,
        UnityAssetParseResult currentParsed,
        SerializedReferenceScanContext context,
        out string toTypeId)
    {
        toTypeId = null!;
        if (!string.IsNullOrWhiteSpace(reference.Guid))
            return TryResolveCrossFileTypeId(reference, context, out toTypeId);

        if (!TryResolveMonoBehaviourScriptGuid(reference.FileId, currentParsed, out var localScriptGuid))
            return false;
        return context.GuidIndex.TryGetTypeId(localScriptGuid, out toTypeId);
    }

    static bool TryResolveCrossFileTypeId(
        UnityObjectReference reference,
        SerializedReferenceScanContext context,
        out string toTypeId)
    {
        toTypeId = null!;
        if (!context.GuidIndex.TryGetAssetPath(reference.Guid!, out var targetAsset))
            return false;
        if (!context.TryGetParsedAsset(targetAsset, out var targetParsed))
            return false;
        if (!TryResolveMonoBehaviourScriptGuid(reference.FileId, targetParsed, out var scriptGuid))
            return false;
        return context.GuidIndex.TryGetTypeId(scriptGuid, out toTypeId);
    }

    static bool TryResolveMonoBehaviourScriptGuid(
        long fileId,
        UnityAssetParseResult parsed,
        out string scriptGuid)
    {
        scriptGuid = null!;
        if (!parsed.MonoBehaviours.TryGetValue(fileId, out var block))
            return false;
        scriptGuid = block.ScriptGuid;
        return !string.IsNullOrWhiteSpace(scriptGuid);
    }
}
