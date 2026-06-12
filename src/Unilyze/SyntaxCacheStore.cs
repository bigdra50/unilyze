using System.Text.Json;

namespace Unilyze;

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
            File.Move(tempPath, manifestPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch (IOException) { /* best effort */ }
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
