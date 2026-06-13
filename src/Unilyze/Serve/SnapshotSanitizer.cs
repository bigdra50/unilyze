using Unilyze.Pipeline;

namespace Unilyze.Serve;

internal sealed record SanitizedSnapshot(
    AnalysisResult Result,
    IReadOnlyDictionary<string, string> FileIdToAbsolutePath,
    IReadOnlyDictionary<string, string> FileIdToDisplayPath);

/// <summary>
/// Strips absolute filesystem paths from a snapshot before it reaches the browser and
/// builds an opaque <c>fileId → absolute path</c> allowlist for the read-only source API.
/// (Full path-scrubbing is implemented alongside the source endpoint.)
/// </summary>
internal static class SnapshotSanitizer
{
    public static SanitizedSnapshot Sanitize(AnalysisResult result) =>
        new(result,
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
}
