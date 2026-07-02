using Unilyze.Discovery;
using Unilyze.Config;
using Unilyze.Detectors;
namespace Unilyze.Pipeline;

internal sealed record AnalysisBuildOptions(
    string Path,
    string? Prefix = null,
    string? AssemblyFilter = null,
    IReadOnlyList<string>? ExcludeDirectories = null,
    AnalysisLevel? RequestedLevel = null,
    bool ExcludeGeneratedCode = true,
    bool ApplyAnyDepthExcludes = true,
    bool IncludeApiSurface = false,
    IAnalysisLogSink? LogSink = null,
    ResolvedAnalysisConfig? AnalysisConfig = null,
    int? MaxParallelism = null,
    bool ResolveNuget = false,
    bool IncludeGenerated = false,
    string? TargetFramework = null,
    bool Incremental = false)
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

    // Incremental enrichment now applies at every analysis level: at syntax level it reuses
    // per-type syntactic metrics, at semantic levels it reuses per-type cohesion/smell payloads
    // while the dependency/coupling graph and global aggregation are always rebuilt full.
    public bool UseIncrementalCache => Incremental;
}
