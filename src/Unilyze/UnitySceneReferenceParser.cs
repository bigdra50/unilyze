using System.Text.RegularExpressions;

namespace Unilyze;

internal sealed record UnityObjectReference(long FileId, string? Guid);

internal sealed record UnityMonoBehaviourBlock(
    long FileId,
    string ScriptGuid,
    Dictionary<string, List<UnityObjectReference>> FieldReferences);

internal sealed record UnityAssetParseResult(
    IReadOnlyDictionary<long, UnityMonoBehaviourBlock> MonoBehaviours);

internal static partial class UnitySceneReferenceParser
{
    const int MonoBehaviourClassId = 114;

    [GeneratedRegex(@"^--- !u!(\d+) &(\d+)$")]
    private static partial Regex DocumentHeaderRegex();

    [GeneratedRegex(@"^\s*m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*([a-fA-F0-9]+)")]
    private static partial Regex ScriptGuidRegex();

    [GeneratedRegex(@"^\s*([A-Za-z_][A-Za-z0-9_]*):\s*\{fileID:\s*(\d+)(?:,\s*guid:\s*([a-fA-F0-9]+))?")]
    private static partial Regex InlineReferenceRegex();

    [GeneratedRegex(@"^\s*-\s*\{fileID:\s*(\d+)(?:,\s*guid:\s*([a-fA-F0-9]+))?")]
    private static partial Regex SequenceReferenceRegex();

    public static UnityAssetParseResult? TryParseFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        using var stream = File.OpenRead(filePath);
        if (!IsTextYaml(stream))
            return null;

        var monoBehaviours = new Dictionary<long, UnityMonoBehaviourBlock>();
        foreach (var document in ReadDocuments(filePath))
        {
            if (!TryParseMonoBehaviourDocument(document, out var block))
                continue;
            monoBehaviours[block.FileId] = block;
        }

        return new UnityAssetParseResult(monoBehaviours);
    }

    static bool IsTextYaml(FileStream stream)
    {
        Span<byte> header = stackalloc byte[5];
        var read = stream.Read(header);
        stream.Position = 0;
        if (read < 5)
            return false;

        return header.SequenceEqual("%YAML"u8);
    }

    static IEnumerable<IReadOnlyList<string>> ReadDocuments(string filePath)
    {
        List<string>? current = null;
        foreach (var line in File.ReadLines(filePath))
        {
            if (DocumentHeaderRegex().IsMatch(line))
            {
                if (current is { Count: > 0 })
                    yield return current;
                current = [line];
                continue;
            }

            current?.Add(line);
        }

        if (current is { Count: > 0 })
            yield return current;
    }

    static bool TryParseMonoBehaviourDocument(IReadOnlyList<string> lines, out UnityMonoBehaviourBlock block)
    {
        block = null!;
        if (lines.Count == 0)
            return false;

        var headerMatch = DocumentHeaderRegex().Match(lines[0]);
        if (!headerMatch.Success
            || !int.TryParse(headerMatch.Groups[1].Value, out var classId)
            || classId != MonoBehaviourClassId
            || !long.TryParse(headerMatch.Groups[2].Value, out var fileId))
            return false;

        string? scriptGuid = null;
        var fieldReferences = new Dictionary<string, List<UnityObjectReference>>(StringComparer.Ordinal);
        string? pendingArrayField = null;

        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            var scriptMatch = ScriptGuidRegex().Match(line);
            if (scriptMatch.Success)
            {
                scriptGuid = scriptMatch.Groups[1].Value;
                pendingArrayField = null;
                continue;
            }

            var inlineMatch = InlineReferenceRegex().Match(line);
            if (inlineMatch.Success)
            {
                var fieldName = inlineMatch.Groups[1].Value;
                if (fieldName.StartsWith("m_", StringComparison.Ordinal))
                {
                    pendingArrayField = null;
                    continue;
                }

                var reference = ParseReference(inlineMatch.Groups[2].Value, inlineMatch.Groups[3].Value);
                AddReference(fieldReferences, fieldName, reference);
                pendingArrayField = null;
                continue;
            }

            var sequenceMatch = SequenceReferenceRegex().Match(line);
            if (sequenceMatch.Success && pendingArrayField is not null)
            {
                var reference = ParseReference(sequenceMatch.Groups[1].Value, sequenceMatch.Groups[2].Value);
                AddReference(fieldReferences, pendingArrayField, reference);
                continue;
            }

            var arrayFieldMatch = ArrayFieldHeaderRegex().Match(line);
            if (arrayFieldMatch.Success)
            {
                pendingArrayField = arrayFieldMatch.Groups[1].Value;
                if (pendingArrayField.StartsWith("m_", StringComparison.Ordinal))
                    pendingArrayField = null;
                continue;
            }

            pendingArrayField = null;
        }

        if (string.IsNullOrWhiteSpace(scriptGuid))
            return false;

        block = new UnityMonoBehaviourBlock(fileId, scriptGuid, fieldReferences);
        return true;
    }

    [GeneratedRegex(@"^\s*([A-Za-z_][A-Za-z0-9_]*):\s*$")]
    private static partial Regex ArrayFieldHeaderRegex();

    static UnityObjectReference ParseReference(string fileIdText, string guidText)
    {
        _ = long.TryParse(fileIdText, out var fileId);
        var guid = string.IsNullOrWhiteSpace(guidText) ? null : guidText;
        return new UnityObjectReference(fileId, guid);
    }

    static void AddReference(
        Dictionary<string, List<UnityObjectReference>> fieldReferences,
        string fieldName,
        UnityObjectReference reference)
    {
        if (!fieldReferences.TryGetValue(fieldName, out var refs))
        {
            refs = [];
            fieldReferences[fieldName] = refs;
        }

        refs.Add(reference);
    }
}
