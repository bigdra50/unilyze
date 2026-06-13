namespace Unilyze.Serve;

internal static class SourcePathBoundary
{
    static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static IReadOnlyList<string> ResolveAllowedRoots(IEnumerable<string> roots)
    {
        var resolved = new HashSet<string>(PathComparer);
        foreach (var root in roots)
        {
            if (TryResolveExistingPath(root, expectDirectory: true, out var canonical))
                resolved.Add(TrimEndingDirectorySeparator(canonical));
        }
        return resolved.ToList();
    }

    public static bool TryResolveAllowedFile(
        string path,
        IReadOnlyList<string> allowedRoots,
        out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (!TryResolveExistingPath(path, expectDirectory: false, out var canonical))
            return false;
        if (!allowedRoots.Any(root => IsWithinRoot(canonical, root)))
            return false;
        resolvedPath = canonical;
        return true;
    }

    static bool TryResolveExistingPath(string path, bool expectDirectory, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var pathRoot = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(pathRoot))
                return false;

            var current = pathRoot;
            var segments = fullPath[pathRoot.Length..].Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length; i++)
            {
                current = Path.Combine(current, segments[i]);
                var isLast = i == segments.Length - 1;
                FileSystemInfo info = isLast && !expectDirectory
                    ? new FileInfo(current)
                    : new DirectoryInfo(current);
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null)
                    current = Path.GetFullPath(target.FullName);
            }

            if (expectDirectory ? !Directory.Exists(current) : !File.Exists(current))
                return false;

            resolvedPath = Path.GetFullPath(current);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static bool IsWithinRoot(string path, string root)
    {
        if (string.Equals(path, root, PathComparison))
            return true;
        return path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);
    }

    static string TrimEndingDirectorySeparator(string path)
    {
        var root = Path.GetPathRoot(path);
        return string.Equals(path, root, PathComparison)
            ? path
            : Path.TrimEndingDirectorySeparator(path);
    }
}
