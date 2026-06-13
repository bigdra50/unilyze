using Unilyze.Pipeline;

namespace Unilyze.Serve;

internal sealed record SanitizedSnapshot(
    AnalysisResult Result,
    IReadOnlyDictionary<string, string> FileIdToAbsolutePath,
    IReadOnlyDictionary<string, string> FileIdToDisplayPath);

/// <summary>
/// Strips absolute filesystem paths from a snapshot before it reaches the browser and
/// builds an opaque <c>fileId → absolute path</c> allowlist for the read-only source API.
/// Types/metrics expose only an opaque <c>fileId</c> in place of their absolute path; the
/// client resolves it back to a body via <c>/api/source</c>, which exact-matches the
/// allowlist. ProjectPath is reduced to a display name and assembly directories are dropped.
/// </summary>
internal static class SnapshotSanitizer
{
    public static SanitizedSnapshot Sanitize(AnalysisResult result)
    {
        var projectRoot = result.ProjectPath;
        var idByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        var absById = new Dictionary<string, string>(StringComparer.Ordinal);
        var displayById = new Dictionary<string, string>(StringComparer.Ordinal);

        string? FileIdFor(string? absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return null;
            if (idByPath.TryGetValue(absolutePath, out var existing))
                return existing;

            var id = "f" + idByPath.Count.ToString();
            idByPath[absolutePath] = id;
            absById[id] = absolutePath;
            displayById[id] = ToDisplayPath(projectRoot, absolutePath);
            return id;
        }

        var types = result.Types
            .Select(t => t with { FilePath = FileIdFor(t.FilePath) ?? string.Empty })
            .ToList();

        var metrics = result.TypeMetrics?
            .Select(m => m with { FilePath = FileIdFor(m.FilePath) })
            .ToList();

        var assemblies = result.Assemblies
            .Select(a => a with { Directory = string.Empty })
            .ToList();

        var sanitized = result with
        {
            ProjectPath = Path.GetFileName(projectRoot.TrimEnd('/', '\\')),
            Types = types,
            TypeMetrics = metrics,
            Assemblies = assemblies,
        };

        return new SanitizedSnapshot(sanitized, absById, displayById);
    }

    static string ToDisplayPath(string projectRoot, string absolutePath)
    {
        try
        {
            var rel = Path.GetRelativePath(projectRoot, absolutePath);
            return rel.Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return Path.GetFileName(absolutePath);
        }
    }
}
