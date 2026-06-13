namespace Unilyze.Discovery;

/// <summary>
/// Internal directory glob matcher supporting <c>*</c> and <c>**</c> segments.
/// No external globbing dependency — Roslyn is the only runtime dependency.
/// </summary>
internal static class DirectoryGlobMatcher
{
    public static IReadOnlyList<(string Pattern, string Path)> Expand(IEnumerable<string> patterns)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var matches = new List<(string Pattern, string Path)>();

        foreach (var pattern in patterns)
            CollectPatternMatches(pattern, seen, matches);

        return matches.OrderBy(m => m.Path, StringComparer.Ordinal).ToList();
    }

    static void CollectPatternMatches(
        string pattern,
        HashSet<string> seen,
        List<(string Pattern, string Path)> matches)
    {
        foreach (var path in ExpandPattern(pattern))
        {
            if (!seen.Add(path))
                continue;
            matches.Add((pattern, path));
        }
    }

    static IEnumerable<string> ExpandPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            yield break;

        var wildcardIndex = pattern.IndexOf('*');
        if (wildcardIndex < 0)
        {
            var existing = ResolveExistingDirectory(pattern);
            if (existing is not null)
                yield return existing;
            yield break;
        }

        var baseDir = ResolveGlobBaseDir(pattern, wildcardIndex);
        var globPart = pattern[wildcardIndex..];
        var segments = globPart.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var match in MatchSegments(baseDir, segments, 0))
            yield return Path.GetFullPath(match);
    }

    static string? ResolveExistingDirectory(string pattern)
    {
        var path = Path.GetFullPath(pattern);
        return Directory.Exists(path) ? path : null;
    }

    static string ResolveGlobBaseDir(string pattern, int wildcardIndex)
    {
        var dirPart = pattern[..wildcardIndex]
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/');
        return string.IsNullOrEmpty(dirPart)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(dirPart);
    }

    static IEnumerable<string> MatchSegments(string currentDir, string[] segments, int index)
    {
        if (index >= segments.Length)
            return YieldIfDirectoryExists(currentDir);

        var segment = segments[index];
        return segment == "**"
            ? MatchDoubleStar(currentDir, segments, index)
            : MatchLiteralSegment(currentDir, segments, index, segment);
    }

    static IEnumerable<string> YieldIfDirectoryExists(string currentDir)
    {
        if (Directory.Exists(currentDir))
            yield return currentDir;
    }

    static IEnumerable<string> MatchDoubleStar(string currentDir, string[] segments, int index)
    {
        foreach (var match in MatchSegments(currentDir, segments, index + 1))
            yield return match;

        if (!Directory.Exists(currentDir))
            yield break;

        foreach (var child in Directory.EnumerateDirectories(currentDir))
        {
            foreach (var match in MatchSegments(child, segments, index + 1))
                yield return match;
            foreach (var match in MatchSegments(child, segments, index))
                yield return match;
        }
    }

    static IEnumerable<string> MatchLiteralSegment(
        string currentDir,
        string[] segments,
        int index,
        string segment)
    {
        if (!Directory.Exists(currentDir))
            yield break;

        foreach (var child in Directory.EnumerateDirectories(currentDir))
        {
            var name = Path.GetFileName(child);
            if (!SegmentMatches(name, segment))
                continue;

            foreach (var match in MatchSegments(child, segments, index + 1))
                yield return match;
        }
    }

    static bool SegmentMatches(string name, string pattern)
    {
        if (!pattern.Contains('*'))
            return name.Equals(pattern, StringComparison.Ordinal);

        if (pattern == "*")
            return true;

        return MatchWildcardSegment(name, pattern);
    }

    static bool MatchWildcardSegment(string name, string pattern)
    {
        var parts = pattern.Split('*', StringSplitOptions.None);
        if (parts.Length == 2 && parts[0].Length == 0 && parts[1].Length == 0)
            return true;

        var pos = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0)
                continue;

            if (!TryAdvanceMatch(name, part, i == 0, ref pos))
                return false;
        }

        var lastPart = parts[^1];
        return lastPart.Length == 0 || name.EndsWith(lastPart, StringComparison.Ordinal);
    }

    static bool TryAdvanceMatch(string name, string part, bool mustStartAtPos, ref int pos)
    {
        var idx = name.IndexOf(part, pos, StringComparison.Ordinal);
        if (idx < 0)
            return false;

        if (mustStartAtPos && idx != 0)
            return false;

        pos = idx + part.Length;
        return true;
    }

    public static string DeriveProjectName(string pattern, string matchPath)
    {
        var wildcardIndex = pattern.IndexOf('*');
        if (wildcardIndex < 0)
            return SanitizeName(Path.GetFileName(matchPath));

        var globBase = ResolveGlobBaseDir(pattern, wildcardIndex);
        var relative = Path.GetRelativePath(globBase, Path.GetFullPath(matchPath));
        return SanitizeName(relative);
    }

    static string SanitizeName(string relativePath)
        => relativePath
            .Replace(Path.DirectorySeparatorChar, '-')
            .Replace(Path.AltDirectorySeparatorChar, '-');
}
