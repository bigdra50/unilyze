namespace Unilyze.Serve;

internal enum ServePhase
{
    /// <summary>Analysis is in flight; the served snapshot (if any) is the previous one.</summary>
    Analyzing,

    /// <summary>The latest analysis succeeded and is the current snapshot.</summary>
    Ready,

    /// <summary>The latest analysis failed; the previous snapshot is retained and stale.</summary>
    Failed,
}

/// <summary>Per-generation measurements, surfaced so Phase 2 perf work is data-driven.</summary>
internal sealed record ServeAnalysisMetrics(
    double AnalysisMillis,
    int JsonSizeBytes,
    double? SanitizeMillis = null,
    double? SerializeMillis = null);

internal sealed record ServeDeltaScore(double Score, int LowRiskCount, int HighRiskCount);

/// <summary>
/// The heavy, generation-independent product of one successful analysis. The store wraps
/// it with a generation number when it is published, producing a <see cref="ServeSnapshot"/>.
/// </summary>
internal sealed record ServeSnapshotContent(
    byte[] JsonBytes,
    string ETag,
    DateTimeOffset AnalyzedAtUtc,
    ServeAnalysisMetrics Metrics,
    IReadOnlyDictionary<string, string> FileIdToAbsolutePath,
    IReadOnlyDictionary<string, string> FileIdToDisplayPath,
    IReadOnlyList<string> AllowedSourceRoots,
    ServeDeltaScore? DeltaScore = null,
    // Opaque fileIds whose source the user edited since the previous snapshot, so the live
    // viewer can pan/highlight the changed blocks. Empty on the first snapshot (no baseline).
    IReadOnlyList<string>? ChangedFileIds = null);

/// <summary>An immutable successful snapshot, tagged with the generation it was published at.</summary>
internal sealed record ServeSnapshot(long Generation, ServeSnapshotContent Content)
{
    public byte[] JsonBytes => Content.JsonBytes;

    public string ETag => Content.ETag;
}

/// <summary>
/// A consistent view of session state for <c>GET /api/state</c>. <see cref="Generation"/>
/// advances on every meaningful transition (analysis started, succeeded, or failed), so a
/// long-poll can return as soon as something the client cares about has changed.
/// </summary>
internal sealed record ServeStateView(
    long Generation,
    ServePhase Phase,
    long? SnapshotGeneration,
    string? SnapshotETag,
    DateTimeOffset? LastSuccessUtc,
    string? LastErrorCode,
    string? LastError,
    ServeAnalysisMetrics? LastMetrics,
    ServeDeltaScore? DeltaScore = null);
