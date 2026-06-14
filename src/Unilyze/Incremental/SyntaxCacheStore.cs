using Unilyze.Metrics;
using Unilyze.Pipeline;
using System.Text.Json;

namespace Unilyze.Incremental;

internal static class SyntaxCacheStore
{
    public const string CacheRootSegment = ".unilyze/cache";
    public const string SyntaxCacheSegment = "syntax/v1";
    public const string ManifestFileName = "manifest.json";

    public static string GetCacheDirectory(string projectRoot) =>
        Path.Combine(projectRoot, CacheRootSegment, SyntaxCacheSegment);

    public static string GetManifestPath(string projectRoot) =>
        Path.Combine(GetCacheDirectory(projectRoot), ManifestFileName);

    public static void EnsureGitIgnore(string projectRoot)
    {
        var gitIgnorePath = Path.Combine(projectRoot, CacheRootSegment, ".gitignore");
        var parent = Path.GetDirectoryName(gitIgnorePath)!;
        Directory.CreateDirectory(parent);
        if (File.Exists(gitIgnorePath))
            return;
        File.WriteAllText(gitIgnorePath, "*\n");
    }

    public static SyntaxCacheManifest? TryLoad(string projectRoot, string expectedFingerprint)
    {
        var manifestPath = GetManifestPath(projectRoot);
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize(json, SyntaxCacheJsonContext.Default.SyntaxCacheManifest);
            if (manifest is null
                || manifest.SchemaVersion != SyntaxCacheFingerprint.SchemaVersion
                || !string.Equals(manifest.Fingerprint, expectedFingerprint, StringComparison.Ordinal))
            {
                return null;
            }

            return manifest;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(string projectRoot, SyntaxCacheManifest manifest)
    {
        EnsureGitIgnore(projectRoot);
        var cacheDir = GetCacheDirectory(projectRoot);
        Directory.CreateDirectory(cacheDir);

        var manifestPath = GetManifestPath(projectRoot);
        var tempPath = manifestPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var json = JsonSerializer.Serialize(manifest, SyntaxCacheJsonContext.Default.SyntaxCacheManifest);
            File.WriteAllText(tempPath, json);
            ReplaceManifest(tempPath, manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Persisting the cache is a best-effort optimization, not a correctness
            // requirement. Concurrent analyses on Windows can transiently hold the
            // manifest open (an open read handle blocks the atomic replace, surfacing
            // as "Access to the path is denied"), so a write that loses the race must
            // never fail the analysis itself.
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch (IOException) { /* best effort */ }
                catch (UnauthorizedAccessException) { /* best effort */ }
            }
        }
    }

    // The temp-then-rename keeps the manifest atomic (readers see either the old or
    // new file, never a partial write). On Windows the rename can still lose a race
    // against another process that has the manifest open, so retry briefly before
    // letting the caller treat it as a best-effort miss.
    static void ReplaceManifest(string tempPath, string manifestPath)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tempPath, manifestPath, overwrite: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < maxAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(20 * attempt));
            }
        }
    }
}

internal static class SyntaxCacheMetrics
{
    public static TypeMetrics StripCouplingFields(TypeMetrics metrics) =>
        metrics with
        {
            AfferentCoupling = null,
            EfferentCoupling = null,
            Instability = null
        };

    public static TypeMetrics ApplyCouplingFields(
        TypeMetrics metrics,
        IReadOnlyDictionary<string, CouplingInfo> couplingMap)
    {
        if (!couplingMap.TryGetValue(TypeIdentity.GetTypeId(metrics), out var coupling))
            return metrics;

        return metrics with
        {
            AfferentCoupling = coupling.AfferentCoupling,
            EfferentCoupling = coupling.EfferentCoupling,
            Instability = coupling.Instability.HasValue ? Math.Round(coupling.Instability.Value, 2) : null
        };
    }
}
