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
    string? TargetFramework);
