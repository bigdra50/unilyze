namespace Unilyze;

internal sealed record AnalysisBuildOptions(
    string Path,
    string? Prefix = null,
    string? AssemblyFilter = null,
    IReadOnlyList<string>? ExcludeDirectories = null,
    AnalysisLevel? RequestedLevel = null,
    bool ExcludeGeneratedCode = true,
    bool ApplyAnyDepthExcludes = true,
    IAnalysisLogSink? LogSink = null,
    EffectiveSmellThresholds? Thresholds = null,
    IReadOnlySet<CodeSmellKind>? DisabledRuleKinds = null,
    bool DisableCycles = false)
{
    static readonly HashSet<CodeSmellKind> EmptyDisabledRules = [];

    public EffectiveSmellThresholds EffectiveThresholds => Thresholds ?? EffectiveSmellThresholds.Default;

    public IReadOnlySet<CodeSmellKind> EffectiveDisabledRules => DisabledRuleKinds ?? EmptyDisabledRules;

    public IAnalysisLogSink EffectiveLog => LogSink ?? new ConsoleAnalysisLogSink(quiet: false);

    public AnalysisLevel EffectiveCap => RequestedLevel ?? AnalysisLevel.Complete;
}
