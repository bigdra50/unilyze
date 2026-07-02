using Unilyze.Discovery;

namespace Unilyze.Serve;

/// <summary>
/// Resolved configuration for a single <c>unilyze serve</c> session. Mirrors the
/// analyze option surface that affects the snapshot, plus the serve-only knobs
/// (<see cref="Port"/>, <see cref="NoOpen"/>). serve always runs full analysis
/// (<c>incremental:false</c>); incremental is syntax-only and out of scope for Phase 1.
/// </summary>
internal sealed record ServeOptions(
    string Path,
    int? Port,
    bool NoOpen,
    AnalysisLevel? RequestedLevel,
    string? Profile,
    IReadOnlyList<string> ExcludeDirs,
    string? Prefix,
    string? Assembly,
    bool ResolveNuget,
    bool IncludeGenerated,
    string? TargetFramework,
    // Shadow verification (design doc §7.3, tasks/reverse-dependency-index-design.md): every N
    // SnapshotBuilder.Build() calls, additionally run a full (non-incremental) analysis and diff
    // it against the incremental result, logging any divergence. Null (the CLI default) disables
    // it — the extra full run roughly doubles analysis cost on the sampled generations, so this
    // stays strictly opt-in.
    int? VerifyIncrementalEveryN = null);
