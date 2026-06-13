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

    public SnapshotBuilder(ServeOptions options) => _options = options;

    public ServeSnapshotContent Build()
    {
        var sw = Stopwatch.StartNew();
        var result = RunAnalysis();
        var analysisMillis = sw.Elapsed.TotalMilliseconds;

        var sanitized = SnapshotSanitizer.Sanitize(result);
        var json = JsonSerializer.Serialize(sanitized.Result, AnalysisJsonContext.Default.AnalysisResult);
        var bytes = Encoding.UTF8.GetBytes(json);

        return new ServeSnapshotContent(
            JsonBytes: bytes,
            ETag: ComputeETag(bytes),
            AnalyzedAtUtc: DateTimeOffset.UtcNow,
            Metrics: new ServeAnalysisMetrics(analysisMillis, bytes.Length),
            FileIdToAbsolutePath: sanitized.FileIdToAbsolutePath,
            FileIdToDisplayPath: sanitized.FileIdToDisplayPath);
    }

    AnalysisResult RunAnalysis()
    {
        var projectRoot = ProgramHelpers.ResolveProjectRoot(_options.Path);
        var config = UnilyzeConfig.LoadMerged(projectRoot, _options.ExcludeDirs, _options.Profile);
        var referenceOpts = BuildReferenceOpts();
        var referenceSettings = ProgramHelpers.LoadReferenceAnalysisSettings(projectRoot, referenceOpts);
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
            ProgramHelpers.TryApplyBaseline(result, projectRoot, baselinePath, out result);

        var emptyOpts = new Dictionary<string, string>();
        var triagePath = TriageApplication.ResolvePath(emptyOpts, config, projectRoot);
        TriageApplication.TryApply(result, triagePath, out result);

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
