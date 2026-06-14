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
/// Runs one full analysis (Phase 1 is always <c>incremental:false</c> — incremental is
/// syntax-only) and turns the result into a serve snapshot. The analysis path mirrors the
/// one-shot <c>analyze</c> command (config merge, reference resolution, baseline, triage)
/// so the live view matches what <c>analyze</c> would produce.
/// </summary>
internal sealed class SnapshotBuilder
{
    readonly ServeOptions _options;
    readonly string _projectRoot;

    public SnapshotBuilder(ServeOptions options)
    {
        _options = options;
        _projectRoot = ProgramHelpers.ResolveProjectRoot(options.Path);
    }

    public ServeSnapshotContent Build()
    {
        var sw = Stopwatch.StartNew();
        var result = RunAnalysis();
        var analysisMillis = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var sanitized = SnapshotSanitizer.Sanitize(result, [_projectRoot]);
        var sanitizeMillis = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var json = JsonSerializer.Serialize(sanitized.Result, AnalysisJsonContext.Default.AnalysisResult);
        var bytes = Encoding.UTF8.GetBytes(json);
        var serializeMillis = sw.Elapsed.TotalMilliseconds;

        return new ServeSnapshotContent(
            JsonBytes: bytes,
            ETag: ComputeETag(bytes),
            AnalyzedAtUtc: DateTimeOffset.UtcNow,
            Metrics: new ServeAnalysisMetrics(analysisMillis, bytes.Length, sanitizeMillis, serializeMillis),
            FileIdToAbsolutePath: sanitized.FileIdToAbsolutePath,
            FileIdToDisplayPath: sanitized.FileIdToDisplayPath,
            AllowedSourceRoots: sanitized.AllowedSourceRoots);
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

    AnalysisResult RunAnalysis()
    {
        var config = UnilyzeConfig.LoadMerged(_projectRoot, _options.ExcludeDirs, _options.Profile);
        var referenceOpts = BuildReferenceOpts();
        var referenceSettings = ProgramHelpers.LoadReferenceAnalysisSettings(_projectRoot, referenceOpts);
        var resolved = config.ResolveAnalysisConfig();

        var result = AnalysisPipeline.Build(
            _options.Path, _options.Prefix, _options.Assembly, config.ExcludeDirs, _options.RequestedLevel,
            excludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
            applyAnyDepthExcludes: !config.DisableDefaultExcludes,
            includeApiSurface: false,
            analysisConfig: resolved,
            maxParallelism: config.MaxParallelism,
            resolveNuget: referenceSettings.ResolveNuget,
            includeGenerated: referenceSettings.IncludeGenerated,
            targetFramework: referenceSettings.TargetFramework,
            incremental: false);

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
