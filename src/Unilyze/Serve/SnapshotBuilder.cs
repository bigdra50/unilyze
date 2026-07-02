using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Unilyze.Cli;
using Unilyze.Config;
using Unilyze.Findings;
using Unilyze.Pipeline;

namespace Unilyze.Serve;

/// <summary>
/// Runs one analysis (<c>incremental:true</c>: a warm edit re-enriches only the changed types
/// at the resolved semantic level, falling back to a full re-enrich on any structural change;
/// the dependency/coupling graph and global aggregation are always rebuilt full, so the result
/// is identical to a non-incremental run) and turns it into a serve snapshot. The analysis path
/// mirrors the one-shot <c>analyze</c> command (config merge, reference resolution, baseline,
/// triage) so the live view matches what <c>analyze</c> would produce.
///
/// When <see cref="ServeOptions.VerifyIncrementalEveryN"/> is set, every Nth <see cref="Build"/>
/// call additionally runs a full (non-incremental) analysis and diffs it against the incremental
/// one via <see cref="ServeVerifyIncremental"/> (design doc §7.3), logging any divergence to
/// stderr. Off by default and never affects the served result.
/// </summary>
internal sealed class SnapshotBuilder
{
    readonly ServeOptions _options;
    readonly string _projectRoot;

    // Input stamps captured at the previous build, diffed on the next build to learn which
    // source files the user edited. Only touched on the single analysis worker thread
    // (AnalysisCoordinator.Loop), so no synchronization is needed.
    IReadOnlyDictionary<string, string>? _previousStamps;

    // "Generation" for --verify-incremental purposes: this builder's own call count, incremented
    // once per Build(). Deliberately independent of SnapshotStore's published-snapshot Generation
    // (which only advances on success) — sampling by call count is simpler to reason about and
    // keeps this class decoupled from the store. Only touched on the single analysis worker
    // thread, like _previousStamps above.
    int _generationCount;

    public SnapshotBuilder(ServeOptions options)
    {
        _options = options;
        _projectRoot = ProgramHelpers.ResolveProjectRoot(options.Path);
    }

    public ServeSnapshotContent Build()
    {
        var sw = Stopwatch.StartNew();
        var rawResult = RunAnalysis(incremental: true);
        var result = ApplyBaselineAndTriage(rawResult);
        var analysisMillis = sw.Elapsed.TotalMilliseconds; // excludes shadow verification below

        _generationCount++;
        MaybeRunShadowVerification(rawResult);

        sw.Restart();
        var sanitized = SnapshotSanitizer.Sanitize(result, [_projectRoot]);
        var sanitizeMillis = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var json = JsonSerializer.Serialize(sanitized.Result, AnalysisJsonContext.Default.AnalysisResult);
        var bytes = Encoding.UTF8.GetBytes(json);
        var serializeMillis = sw.Elapsed.TotalMilliseconds;

        var changedFileIds = DetectChangedFileIds(sanitized.FileIdToDisplayPath);

        return new ServeSnapshotContent(
            JsonBytes: bytes,
            ETag: ComputeETag(bytes),
            AnalyzedAtUtc: DateTimeOffset.UtcNow,
            Metrics: new ServeAnalysisMetrics(analysisMillis, bytes.Length, sanitizeMillis, serializeMillis),
            FileIdToAbsolutePath: sanitized.FileIdToAbsolutePath,
            FileIdToDisplayPath: sanitized.FileIdToDisplayPath,
            AllowedSourceRoots: sanitized.AllowedSourceRoots,
            ChangedFileIds: changedFileIds);
    }

    /// <summary>
    /// Captures the current input stamps and diffs them against the previous build via
    /// <see cref="ServeChangedFiles.Detect"/> to learn which source blocks the user edited.
    /// </summary>
    IReadOnlyList<string> DetectChangedFileIds(IReadOnlyDictionary<string, string> fileIdToDisplayPath)
    {
        var current = ServeInputFingerprint.ComputeStamps(_projectRoot);
        var previous = _previousStamps;
        _previousStamps = current;
        return ServeChangedFiles.Detect(previous, current, fileIdToDisplayPath);
    }

    public IReadOnlyList<string> ResolveWatchedInputPaths()
    {
        var paths = new List<string>
        {
            UnilyzeConfig.GetGlobalConfigPath(),
            UnilyzeConfig.GetProjectConfigPath(_projectRoot),
        };
        var config = UnilyzeConfig.LoadMerged(_projectRoot, _options.ExcludeDirs, _options.Profile);
        if (config.Baseline is { } baselinePath)
            paths.Add(BaselineFile.ResolvePath(_projectRoot, baselinePath));
        paths.Add(config.Triage is { } triagePath
            ? TriageFile.ResolvePath(_projectRoot, triagePath)
            : TriageFile.DefaultPath(_projectRoot));
        return paths;
    }

    // `incremental` selects the analysis path only: RunAnalysis always returns the RAW pipeline
    // result, before baseline/triage post-processing. Build() applies baseline/triage itself, once,
    // to the primary (incremental: true) result — MaybeRunShadowVerification below compares two
    // RAW results, so an active baseline/triage file never shows up as a false-positive divergence
    // (baseline/triage application is deterministic post-processing outside RDI's invalidation
    // scope; comparing before it also means the shadow run never depends on baseline/triage file
    // I/O succeeding a second time).
    AnalysisResult RunAnalysis(bool incremental)
    {
        var config = UnilyzeConfig.LoadMerged(_projectRoot, _options.ExcludeDirs, _options.Profile);
        var referenceOpts = BuildReferenceOpts();
        var referenceSettings = ProgramHelpers.LoadReferenceAnalysisSettings(_projectRoot, referenceOpts);
        var resolved = config.ResolveAnalysisConfig();

        return AnalysisPipeline.Build(
            _options.Path, _options.Prefix, _options.Assembly, config.ExcludeDirs, _options.RequestedLevel,
            excludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
            applyAnyDepthExcludes: !config.DisableDefaultExcludes,
            includeApiSurface: false,
            analysisConfig: resolved,
            maxParallelism: config.MaxParallelism,
            resolveNuget: referenceSettings.ResolveNuget,
            includeGenerated: referenceSettings.IncludeGenerated,
            targetFramework: referenceSettings.TargetFramework,
            incremental: incremental);
    }

    // Shadow verification (design doc §7.3): opt-in (ServeOptions.VerifyIncrementalEveryN is null
    // by default), and — even when enabled — sampled every N generations so the extra full
    // analysis doesn't double the cost of every warm edit. Never throws and never affects the
    // primary (incremental) result being served: a divergence is a diagnostic signal for the
    // person running serve with this flag on, not a serving failure.
    void MaybeRunShadowVerification(AnalysisResult rawIncrementalResult)
    {
        if (_options.VerifyIncrementalEveryN is not { } everyN || everyN <= 0)
            return;
        if (_generationCount % everyN != 0)
            return;

        try
        {
            var rawFullResult = RunAnalysis(incremental: false);
            var report = ServeVerifyIncremental.Compare(rawFullResult, rawIncrementalResult);
            if (report.Diverged)
                Console.Error.WriteLine($"[incremental] DIVERGENCE: {string.Join(", ", report.TypeIds)}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // The shadow run is a diagnostic safety net — its own failure must never affect the
            // primary snapshot Build() already computed from the incremental result.
            Console.Error.WriteLine($"[incremental] shadow verification failed: {ex.Message}");
        }
    }

    AnalysisResult ApplyBaselineAndTriage(AnalysisResult result)
    {
        var config = UnilyzeConfig.LoadMerged(_projectRoot, _options.ExcludeDirs, _options.Profile);

        var baselinePath = config.Baseline;
        if (baselinePath is not null)
        {
            if (ProgramHelpers.TryApplyBaseline(
                    result, _projectRoot, baselinePath, out result) is 1)
            {
                throw new ServeAnalysisException(
                    "BASELINE_APPLY_FAILED",
                    "The configured baseline could not be applied.",
                    $"Failed to apply baseline '{BaselineFile.ResolvePath(_projectRoot, baselinePath)}'.");
            }
        }

        var emptyOpts = new Dictionary<string, string>();
        var triagePath = TriageApplication.ResolvePath(emptyOpts, config, _projectRoot);
        if (TriageApplication.TryApply(result, triagePath, out result) is 1)
        {
            throw new ServeAnalysisException(
                "TRIAGE_APPLY_FAILED",
                "The configured triage file could not be applied.",
                $"Failed to apply triage file '{triagePath}'.");
        }

        return result;
    }

    Dictionary<string, string> BuildReferenceOpts()
    {
        var opts = new Dictionary<string, string>();
        if (_options.ResolveNuget)
            opts["--resolve-nuget"] = "true";
        if (_options.IncludeGenerated)
            opts["--include-generated"] = "true";
        if (_options.TargetFramework is not null)
            opts["--tfm"] = _options.TargetFramework;
        return opts;
    }

    static string ComputeETag(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return "\"" + Convert.ToHexString(hash) + "\"";
    }
}
