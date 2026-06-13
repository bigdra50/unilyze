namespace Unilyze;

internal sealed record ProjectWorkResult(
    string Name,
    string ProjectRoot,
    AnalysisResult Result,
    StatuslineFormatter.Summary Summary,
    ResolvedAnalysisConfig Resolved,
    string? GateLabel = null);

internal sealed record MultiProjectCliContext(
    IReadOnlyDictionary<string, string> Opts,
    IReadOnlyList<string> ProjectGlobs)
{
    public string? OutputDir => Opts.GetValueOrDefault("-o") ?? Opts.GetValueOrDefault("--output");
}

internal sealed record AnalyzeRunContext(
    MultiProjectCliContext Cli,
    AnalysisLevel? RequestedLevel,
    OutputFormat Format,
    IReadOnlyList<string> CliExcludeDirs,
    string? CliProfile,
    string? Prefix,
    string? Assembly);

internal sealed record ProjectAnalysisRequest(
    AnalyzeRunContext Run,
    string Pattern,
    string RawPath);

internal sealed record BadgeRunContext(
    MultiProjectCliContext Cli,
    BadgeSetup Setup);

internal sealed record BadgeProjectRequest(
    BadgeRunContext Run,
    string Pattern,
    string RawPath);

internal sealed record BadgeLoopStateResult(
    List<MultiProjectSummaryEntry> Entries,
    List<(MultiProjectSummaryEntry Entry, string MetricValue)> TableRows,
    bool AnyFailed,
    int? ErrorExit = null);

internal readonly record struct BadgeSetup(
    BadgeMetric Metric,
    BadgeFormat Format,
    AnalysisLevel? RequestedLevel,
    string? FailUnder,
    string? FailOver,
    string? BaselinePath,
    bool UseCodeHealthV1,
    string MetricSlug);
