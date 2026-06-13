namespace Unilyze.Serve;

/// <summary>
/// Decides whether a path under the project root is an analysis input worth reacting to.
/// Analysis depends on more than <c>.cs</c>: <c>.csproj/.sln</c>, <c>.asmdef/.asmref/.meta</c>,
/// reference DLLs, generated sources, and Unity's <c>ProjectVersion.txt</c>/<c>ScriptAssemblies</c>
/// all change the result, so watching only <c>.cs</c> would silently serve stale output.
/// Build/VCS/cache directories are excluded so churn there does not trigger re-analysis.
/// </summary>
internal static class ServeInputFilter
{
    static readonly HashSet<string> RelevantExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".asmdef", ".asmref", ".meta", ".dll",
    };

    static readonly HashSet<string> ExcludedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "obj", "bin", "Temp", ".unilyze", "node_modules", "Logs",
    };

    public static bool IsRelevant(string fullPath, string projectRoot)
    {
        var rel = Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
        if (rel.StartsWith("../", StringComparison.Ordinal) || rel == "..")
            return false; // outside the watched root

        var segments = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Unity compiles to Library/ScriptAssemblies; everything else under Library is noise.
        var underLibrary = segments.Length > 0
            && string.Equals(segments[0], "Library", StringComparison.OrdinalIgnoreCase);
        if (underLibrary)
        {
            var isScriptAssemblies = segments.Length > 1
                && string.Equals(segments[1], "ScriptAssemblies", StringComparison.OrdinalIgnoreCase);
            if (!isScriptAssemblies)
                return false;
        }

        // Exclude build/VCS/cache directories anywhere in the path.
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (ExcludedSegments.Contains(segments[i]))
                return false;
        }

        var fileName = segments.Length > 0 ? segments[^1] : string.Empty;

        // Unity editor version pins the analysis level / defines.
        if (string.Equals(fileName, "ProjectVersion.txt", StringComparison.OrdinalIgnoreCase)
            && rel.Contains("ProjectSettings", StringComparison.OrdinalIgnoreCase))
            return true;

        return RelevantExtensions.Contains(Path.GetExtension(fileName));
    }
}
