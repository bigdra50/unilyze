namespace Unilyze;

internal static class SerializedReferenceEdgeCollector
{
    public static void CollectFromAsset(
        UnityAssetParseResult parsed,
        SerializedReferenceScanContext context,
        HashSet<(string FromTypeId, string ToTypeId)> edges)
    {
        foreach (var block in parsed.MonoBehaviours.Values)
            CollectFromBlock(block, parsed, context, edges);
    }

    static void CollectFromBlock(
        UnityMonoBehaviourBlock block,
        UnityAssetParseResult parsed,
        SerializedReferenceScanContext context,
        HashSet<(string FromTypeId, string ToTypeId)> edges)
    {
        if (!context.GuidIndex.TryGetTypeId(block.ScriptGuid, out var fromTypeId))
            return;
        if (!context.SerializedFields.TryGetValue(fromTypeId, out var allowedFields))
            return;

        foreach (var (fieldName, references) in block.FieldReferences)
            CollectFromField(fieldName, references, fromTypeId, allowedFields, parsed, context, edges);
    }

    static void CollectFromField(
        string fieldName,
        IReadOnlyList<UnityObjectReference> references,
        string fromTypeId,
        HashSet<string> allowedFields,
        UnityAssetParseResult parsed,
        SerializedReferenceScanContext context,
        HashSet<(string FromTypeId, string ToTypeId)> edges)
    {
        if (!allowedFields.Contains(fieldName))
            return;

        foreach (var reference in references)
        {
            if (reference.FileId == 0)
                continue;
            if (!SerializedReferenceTargetResolver.TryResolveTypeId(reference, parsed, context, out var toTypeId))
                continue;
            if (toTypeId == fromTypeId)
                continue;
            edges.Add((fromTypeId, toTypeId));
        }
    }
}
