namespace Unilyze.Discovery;

/// <summary>What a Unity project folder looks like on disk.</summary>
internal static class UnityProjectLayout
{
    /// <summary>The marker every Unity project carries; what switches analysis to Unity mode.</summary>
    public static bool IsProjectRoot(string directory) =>
        File.Exists(Path.Combine(directory, "ProjectSettings", "ProjectVersion.txt"));

    /// <summary>
    /// Loose on purpose: a project checked out without ProjectSettings, or the synthetic
    /// layouts people reproduce bugs with, still carry Assets next to Library, and Library
    /// must stay out of analysis either way. Root detection stays strict.
    /// </summary>
    public static bool LooksLikeProject(string directory) =>
        Directory.Exists(Path.Combine(directory, "Assets")) || IsProjectRoot(directory);

    /// <summary>
    /// Finds the one Unity project directly below <paramref name="directory"/>. Several
    /// projects are ambiguous and yield false: the caller must name the one it means.
    /// </summary>
    public static bool TryFindSingleProjectBelow(string directory, out string projectDir)
    {
        projectDir = null!;
        string? found = null;
        try
        {
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!IsProjectRoot(child))
                    continue;
                if (found is not null)
                    return false;
                found = child;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder has no project below it that could be analyzed anyway.
            return false;
        }

        if (found is null)
            return false;

        projectDir = found;
        return true;
    }
}
