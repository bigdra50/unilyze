namespace Unilyze.History;

internal static class HotspotPathHelper
{
    internal static FileChangeFrequency? ResolveFileFrequency(
        string? filePath,
        string normalizedProjectPath,
        Dictionary<string, FileChangeFrequency> changeByRelPath)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        var normalizedAbsolute = NormalizePath(Path.GetFullPath(filePath));
        var prefix = NormalizePath(normalizedProjectPath) + "/";
        if (normalizedAbsolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var relative = normalizedAbsolute[prefix.Length..];
            if (changeByRelPath.TryGetValue(relative, out var freq))
                return freq;
        }

        var fileName = Path.GetFileName(filePath);
        foreach (var kv in changeByRelPath)
        {
            if (Path.GetFileName(kv.Key).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        return null;
    }

    internal static string GetRelativePath(string repoPath, string absolutePath)
    {
        var repoFull = Path.GetFullPath(repoPath).TrimEnd(Path.DirectorySeparatorChar);
        var fileFull = Path.GetFullPath(absolutePath);
        var prefix = NormalizePath(repoFull) + "/";
        var normalized = NormalizePath(fileFull);
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return normalized[prefix.Length..];
        return normalized;
    }

    internal static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimEnd('/');
}
