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
    public static string Compute(string projectRoot)
    {
        var entries = new List<string>();
        EnumerateRelevantFiles(projectRoot, entries);
        entries.Sort(StringComparer.Ordinal);

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join('\n', entries)));
        return Convert.ToHexString(bytes);
    }

    static void EnumerateRelevantFiles(string projectRoot, List<string> entries)
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
                entries.Add($"{rel}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // File vanished mid-scan; skip it (the next reconcile will catch up).
            }
        }
    }
}
