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
}
