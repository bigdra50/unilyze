namespace Unilyze.Unity;

internal static class UnityMetaGuidReader
{
    public static string? TryReadGuid(string metaFilePath)
    {
        if (!File.Exists(metaFilePath))
            return null;

        foreach (var line in File.ReadLines(metaFilePath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
                continue;

            var guid = trimmed["guid:".Length..].Trim();
            return string.IsNullOrEmpty(guid) ? null : guid;
        }

        return null;
    }
}
