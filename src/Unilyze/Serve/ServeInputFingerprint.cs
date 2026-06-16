using System.Security.Cryptography;
using System.Text;

namespace Unilyze.Serve;

/// <summary>
/// A content-independent fingerprint of every analysis input under the project root
/// (path + size + last-write time). The periodic reconcile compares fingerprints to
/// recover changes that FileSystemWatcher dropped (buffer overflow, atomic renames,
/// OS quirks); a differing fingerprint means "re-analyze".
/// </summary>
internal static class ServeInputFingerprint
{
    public static string Compute(
        string projectRoot,
        IReadOnlyCollection<string>? explicitInputPaths = null)
    {
        var entries = ComputeStamps(projectRoot, explicitInputPaths)
            .Select(stamp => stamp.Key + "|" + stamp.Value)
            .ToList();
        entries.Sort(StringComparer.Ordinal);

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join('\n', entries)));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// The per-input stamp map underlying <see cref="Compute"/>: key = a stable input
    /// identity (project-relative path for files, <c>explicit:&lt;name&gt;</c> for external
    /// inputs), value = <c>size|mtimeTicks</c> (or <c>missing</c>/<c>unavailable</c>).
    /// serve diffs two stamp maps across successive analyses to learn which files changed.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ComputeStamps(
        string projectRoot,
        IReadOnlyCollection<string>? explicitInputPaths = null)
    {
        var stamps = new Dictionary<string, string>(StringComparer.Ordinal);
        EnumerateRelevantFiles(projectRoot, stamps);
        AppendExplicitInputs(projectRoot, explicitInputPaths, stamps);
        return stamps;
    }

    static void AppendExplicitInputs(
        string projectRoot,
        IReadOnlyCollection<string>? explicitInputPaths,
        Dictionary<string, string> stamps)
    {
        if (explicitInputPaths is not { Count: > 0 })
            return;

        foreach (var path in explicitInputPaths.Distinct(SourcePathBoundary.PathComparer))
        {
            var fullPath = Path.GetFullPath(path);
            try
            {
                var info = new FileInfo(fullPath);
                var name = Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
                stamps["explicit:" + name] = info.Exists
                    ? $"{info.Length}|{info.LastWriteTimeUtc.Ticks}"
                    : "missing";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stamps["explicit:" + fullPath] = "unavailable";
            }
        }
    }

    static void EnumerateRelevantFiles(string projectRoot, Dictionary<string, string> stamps)
    {
        if (!Directory.Exists(projectRoot))
            return;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            if (!ServeInputFilter.IsRelevant(file, projectRoot))
                continue;

            try
            {
                var info = new FileInfo(file);
                var rel = Path.GetRelativePath(projectRoot, file).Replace('\\', '/');
                stamps[rel] = $"{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // File vanished mid-scan; skip it (the next reconcile will catch up).
            }
        }
    }
}
