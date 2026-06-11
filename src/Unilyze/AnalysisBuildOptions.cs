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
    ResolvedAnalysisConfig? AnalysisConfig = null,
    int? MaxParallelism = null)
{
    static readonly ResolvedAnalysisConfig DefaultAnalysisConfig = new(
        EffectiveSmellThresholds.Default,
        SmellThresholdProfiles.DefaultProfileName,
        new HashSet<CodeSmellKind>(),
        DisableCycles: false,
        InformationalSmellKinds: new HashSet<CodeSmellKind>(),
        SmellOverrides: null);

    public ResolvedAnalysisConfig EffectiveAnalysisConfig => AnalysisConfig ?? DefaultAnalysisConfig;

    public IAnalysisLogSink EffectiveLog => LogSink ?? new ConsoleAnalysisLogSink(quiet: false);

    public AnalysisLevel EffectiveCap => RequestedLevel ?? AnalysisLevel.Complete;

    public int EffectiveMaxParallelism => UnilyzeConfig.ResolveMaxParallelism(MaxParallelism);
}
