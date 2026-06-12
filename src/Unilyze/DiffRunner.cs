using System.Globalization;
using System.Text.Json;

namespace Unilyze;

internal static class DiffRunner
{
    private sealed record DiffRunOptions(
        string? BeforePath,
        AnalysisResult? BeforeResult,
        string AfterPath,
        AnalysisResult? AfterResult,
        string? Output,
        OutputFormat Format,
        bool NoOpen,
        bool FailOnRegression,
        bool FailOnVersionMismatch,
        bool ChangedOnly,
        double? FailOnDeltaBelow);

    private sealed record DiffSummaries(
        StatuslineFormatter.Summary? Before,
        StatuslineFormatter.Summary? After);

    private sealed record RegressionGateResult(
        DiffGateResult? Gate,
        int GateExit);

    private sealed record DiffOutputContext(
        string BeforePath,
        string AfterPath,
        string AfterJson,
        string DiffJson,
        AnalysisResult After,
        DiffResult Diff,
        string? Output,
        OutputFormat Format,
        bool NoOpen,
        bool ChangedOnly,
        DiffSummaries Summaries,
        bool FailOnRegression,
        double? FailOnDeltaBelow,
        RegressionGateResult RegressionGate,
        int VersionExit,
        int DeltaExit);

    private sealed record DiffCliInput(
        string? BaseRef,
        string? Output,
        OutputFormat Format,
        bool NoOpen,
        bool FailOnRegression,
        bool FailOnVersionMismatch,
        bool ChangedOnly,
        double? FailOnDeltaBelow,
        string? PathOverride,
        AnalysisLevel? RequestedLevel,
        IReadOnlyList<string> Positional);

    private sealed record BaseRefRequest(
        string BaseRef,
        string AfterPath,
        string? PathOverride,
        AnalysisLevel? RequestedLevel,
        DiffRunOptions Options);

    public static int Run(string[] args)
    {
        if (args.Length == 0 || CliArgValidationSupport.IsHelpRequest(args))
            return PrintUsage();

        var usageError = CliArgValidation.ValidateDiffArgs(args);
        if (usageError != 0)
            return usageError;

        if (ProgramHelpers.HasFlagWithoutValue(args, "--base-ref"))
        {
            Console.Error.WriteLine("--base-ref requires a value");
            return 1;
        }
        if (ProgramHelpers.HasFlagWithoutValue(args, "--fail-on-delta-below"))
        {
            Console.Error.WriteLine("--fail-on-delta-below requires a value");
            return 1;
        }

        var parseError = TryParseDiffCliInput(args, out var input);
        if (parseError != 0)
            return parseError;

        return input.BaseRef != null
            ? ExecuteBaseRefDiff(input)
            : ExecuteFileDiff(input);
    }

    static int TryParseDiffCliInput(string[] args, out DiffCliInput input)
    {
        var opts = ProgramHelpers.ParseOptions(args);
        var levelError = TryParseRequestedLevel(opts.GetValueOrDefault("--level"), out var requestedLevel);
        if (levelError != 0)
        {
            input = null!;
            return levelError;
        }

        var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
        var formatStr = opts.GetValueOrDefault("-f") ?? opts.GetValueOrDefault("--format");
        var formatError = TryResolveDiffFormat(formatStr, output, out var format);
        if (formatError != 0)
        {
            input = null!;
            return formatError;
        }
        var deltaError = TryParseDeltaThreshold(
            opts.GetValueOrDefault("--fail-on-delta-below"), out var failOnDeltaBelow);
        if (deltaError != 0)
        {
            input = null!;
            return deltaError;
        }

        input = new DiffCliInput(
            opts.GetValueOrDefault("--base-ref"),
            output,
            format,
            opts.ContainsKey("--no-open"),
            opts.ContainsKey("--fail-on-regression"),
            opts.ContainsKey("--fail-on-version-mismatch"),
            opts.ContainsKey("--changed-only"),
            failOnDeltaBelow,
            opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path"),
            requestedLevel,
            CliArgValidationSupport.ExtractPositionalArgs(args, CliArgValidation.DiffValueOptions));
        return 0;
    }

    static int TryParseDeltaThreshold(string? value, out double? threshold)
    {
        threshold = null;
        if (value == null)
            return 0;

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || !double.IsFinite(parsed)
            || parsed is < 0 or > 1)
        {
            Console.Error.WriteLine("--fail-on-delta-below must be a number from 0 to 1");
            return 1;
        }

        threshold = parsed;
        return 0;
    }

    static int TryParseRequestedLevel(string? levelStr, out AnalysisLevel? requestedLevel)
    {
        requestedLevel = null;
        if (levelStr == null)
            return 0;

        if (!AnalysisLevelOption.TryParse(levelStr, out var lvl))
        {
            Console.Error.WriteLine($"Unknown level: '{levelStr}'. Valid levels: syntax, core, full, complete");
            return 1;
        }

        requestedLevel = lvl;
        return 0;
    }

    static int TryResolveDiffFormat(string? formatStr, string? output, out OutputFormat format)
    {
        format = OutputFormat.Json;
        try
        {
            format = ProgramHelpers.ResolveFormat(formatStr, output);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (format == OutputFormat.Sarif)
        {
            Console.Error.WriteLine("Diff does not support SARIF output. Use json or html.");
            return 1;
        }

        if (formatStr == null && output == null)
            format = OutputFormat.Json;

        return 0;
    }

    static DiffRunOptions ToDiffRunOptions(DiffCliInput input) =>
        new(null, null, "", null, input.Output, input.Format, input.NoOpen,
            input.FailOnRegression, input.FailOnVersionMismatch, input.ChangedOnly,
            input.FailOnDeltaBelow);

    static int ExecuteBaseRefDiff(DiffCliInput input)
    {
        if (input.Positional.Count != 1)
        {
            Console.Error.WriteLine(
                "Usage: unilyze diff --base-ref <git-ref> <after.json> [-p path] [--level syntax|core|full|complete] ...");
            return 1;
        }

        try
        {
            return RunWithBaseRef(new BaseRefRequest(
                input.BaseRef!, input.Positional[0], input.PathOverride,
                input.RequestedLevel, ToDiffRunOptions(input)));
        }
        catch (Exception ex) when (ex is FileNotFoundException or JsonException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static int ExecuteFileDiff(DiffCliInput input)
    {
        if (input.Positional.Count < 2)
        {
            Console.Error.WriteLine(
                "Usage: unilyze diff <before.json> <after.json> [-o output.{json,html}] [-f html] [--no-open] [--fail-on-regression] [--changed-only]");
            return 1;
        }

        try
        {
            return RunComparison(ToDiffRunOptions(input) with
            {
                BeforePath = input.Positional[0],
                AfterPath = input.Positional[1],
            });
        }
        catch (Exception ex) when (ex is FileNotFoundException or JsonException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static int RunWithBaseRef(BaseRefRequest request)
    {
        try
        {
            var after = LoadAfterSnapshot(request.AfterPath);
            return RunBaseRefComparison(request, after);
        }
        catch (GitWorktreeException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static AnalysisResult LoadAfterSnapshot(string afterPath)
    {
        var afterJson = File.ReadAllText(afterPath);
        return JsonSerializer.Deserialize(afterJson, AnalysisJsonContext.Default.AnalysisResult)
               ?? throw new InvalidOperationException($"Failed to parse: {afterPath}");
    }

    static int RunBaseRefComparison(BaseRefRequest request, AnalysisResult after)
    {
        var projectPath = request.PathOverride ?? after.ProjectPath;
        var beforeLabel = $"base-ref:{request.BaseRef}";

        GitWorktreeSession? session = null;
        try
        {
            session = GitWorktreeSession.Create(projectPath, request.BaseRef);
            var before = AnalyzeBaseRef(session, projectPath, request.RequestedLevel);
            return RunComparison(request.Options with
            {
                BeforePath = beforeLabel,
                BeforeResult = before,
                AfterPath = request.AfterPath,
                AfterResult = after,
            });
        }
        finally
        {
            session?.Dispose();
        }
    }

    static string ResolveBaseProjectPath(GitWorktreeSession session, string projectPath)
    {
        var relative = GitWorktreeSession.GetRepoRelativePath(projectPath);
        return string.IsNullOrEmpty(relative)
            ? session.WorktreePath
            : Path.GetFullPath(Path.Combine(session.WorktreePath, relative));
    }

    static AnalysisResult AnalyzeBaseRef(
        GitWorktreeSession session,
        string projectPath,
        AnalysisLevel? requestedLevel)
    {
        var baseProjectPath = ResolveBaseProjectPath(session, projectPath);
        var projectRoot = ProgramHelpers.ResolveProjectRoot(baseProjectPath);
        var config = UnilyzeConfig.LoadMerged(projectRoot, []);
        var referenceSettings = ReferenceAnalysisSettings.LoadMerged(projectRoot);
        var resolved = config.ResolveAnalysisConfig();
        return AnalysisPipeline.Build(
            baseProjectPath,
            null,
            null,
            config.ExcludeDirs,
            requestedLevel,
            excludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
            applyAnyDepthExcludes: !config.DisableDefaultExcludes,
            analysisConfig: resolved,
            maxParallelism: config.MaxParallelism,
            resolveNuget: referenceSettings.ResolveNuget,
            includeGenerated: referenceSettings.IncludeGenerated,
            targetFramework: referenceSettings.TargetFramework);
    }

    static AnalysisResult ApplyWorkingTreeTriage(AnalysisResult result, string projectPath)
    {
        if (!Directory.Exists(projectPath))
            return result;

        var projectRoot = ProgramHelpers.ResolveProjectRoot(projectPath);

        var config = UnilyzeConfig.LoadMerged(projectRoot);
        var triagePath = TriageApplication.ResolvePath(new Dictionary<string, string>(), config, projectRoot);
        if (triagePath is null)
            return result;

        var triageError = TriageApplication.TryApply(result, triagePath, out var updated);
        return triageError is 1 ? result : updated;
    }

    static int RunComparison(DiffRunOptions options)
    {
        var before = LoadBeforeSnapshot(options);
        var (after, afterJson) = LoadAfterSnapshot(options);
        before = ApplyWorkingTreeTriage(before, after.ProjectPath);
        after = ApplyWorkingTreeTriage(after, after.ProjectPath);

        WarnIfAnalysisLevelsDiffer(before, after);
        WarnIfProfilesDiffer(before, after);
        WarnIfReferenceOptionsDiffer(before, after);

        var versionExit = EvaluateVersionMismatch(before, after, options.FailOnVersionMismatch);

        var diff = DiffCalculator.Compare(before, after);
        var diffJson = JsonSerializer.Serialize(diff, AnalysisJsonContext.Default.DiffResult);

        PrintSummary(diff);
        var deltaExit = EvaluateDeltaGate(diff.DeltaScore, options.FailOnDeltaBelow);

        var summaries = ComputeSummariesIfNeeded(before, after, options.FailOnRegression, options.Format);
        var regressionGate = EvaluateRegressionGate(summaries, options.FailOnRegression);

        return WriteFormattedOutput(new DiffOutputContext(
            options.BeforePath!,
            options.AfterPath,
            afterJson,
            diffJson,
            after,
            diff,
            options.Output,
            options.Format,
            options.NoOpen,
            options.ChangedOnly,
            summaries,
            options.FailOnRegression,
            options.FailOnDeltaBelow,
            regressionGate,
            versionExit,
            deltaExit));
    }

    static int EvaluateDeltaGate(double deltaScore, double? threshold)
    {
        if (threshold == null || deltaScore >= threshold.Value)
            return 0;

        Console.Error.WriteLine(
            $"deltaScore gate failed: {deltaScore.ToString("0.###", CultureInfo.InvariantCulture)} "
            + $"is below {threshold.Value.ToString("0.###", CultureInfo.InvariantCulture)}");
        return 2;
    }

    static AnalysisResult LoadBeforeSnapshot(DiffRunOptions options)
    {
        if (options.BeforeResult != null)
            return options.BeforeResult;

        var beforeJson = File.ReadAllText(options.BeforePath!);
        return JsonSerializer.Deserialize(beforeJson, AnalysisJsonContext.Default.AnalysisResult)
               ?? throw new InvalidOperationException($"Failed to parse: {options.BeforePath}");
    }

    static (AnalysisResult After, string AfterJson) LoadAfterSnapshot(DiffRunOptions options)
    {
        if (options.AfterResult != null)
        {
            return (
                options.AfterResult,
                JsonSerializer.Serialize(options.AfterResult, AnalysisJsonContext.Default.AnalysisResult));
        }

        var afterJson = File.ReadAllText(options.AfterPath);
        var after = JsonSerializer.Deserialize(afterJson, AnalysisJsonContext.Default.AnalysisResult)
                    ?? throw new InvalidOperationException($"Failed to parse: {options.AfterPath}");
        return (after, afterJson);
    }

    static void WarnIfProfilesDiffer(AnalysisResult before, AnalysisResult after)
    {
        var beforeProfile = before.Profile ?? SmellThresholdProfiles.DefaultProfileName;
        var afterProfile = after.Profile ?? SmellThresholdProfiles.DefaultProfileName;
        if (beforeProfile != afterProfile)
        {
            Console.Error.WriteLine(
                $"Warning: profiles differ (before: {beforeProfile}, after: {afterProfile}). "
                + "Smell counts and thresholds may not be comparable.");
        }
    }

    static void WarnIfReferenceOptionsDiffer(AnalysisResult before, AnalysisResult after)
    {
        if (before.ResolveNuget != after.ResolveNuget
            || before.IncludeGenerated != after.IncludeGenerated
            || !string.Equals(before.TargetFramework, after.TargetFramework, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                "Warning: reference-analysis opt-ins differ "
                + $"(before: resolveNuget={before.ResolveNuget ?? false}, includeGenerated={before.IncludeGenerated ?? false}, tfm={before.TargetFramework ?? "auto"}; "
                + $"after: resolveNuget={after.ResolveNuget ?? false}, includeGenerated={after.IncludeGenerated ?? false}, tfm={after.TargetFramework ?? "auto"}). "
                + "Semantic metrics (CBO, DIT, boxing) may not be comparable.");
        }
    }

    static void WarnIfAnalysisLevelsDiffer(AnalysisResult before, AnalysisResult after)
    {
        if (before.AnalysisLevel != after.AnalysisLevel)
        {
            Console.Error.WriteLine(
                $"Warning: analysis levels differ (before: {before.AnalysisLevel ?? "unknown"}, "
                + $"after: {after.AnalysisLevel ?? "unknown"}). Metric deltas may be unreliable.");
        }
    }

    static int EvaluateVersionMismatch(AnalysisResult before, AnalysisResult after, bool failOnVersionMismatch)
    {
        if (before.MetricsVersion != after.MetricsVersion)
        {
            Console.Error.WriteLine(
                $"Warning: metrics versions differ (before: {ToolVersionInfo.FormatMetricsVersion(before.MetricsVersion)}, "
                + $"after: {ToolVersionInfo.FormatMetricsVersion(after.MetricsVersion)}). Metric deltas may be unreliable.");
            if (failOnVersionMismatch)
                return 2;
        }

        return 0;
    }

    static DiffSummaries ComputeSummariesIfNeeded(
        AnalysisResult before,
        AnalysisResult after,
        bool failOnRegression,
        OutputFormat format)
    {
        if (!failOnRegression && format != OutputFormat.Markdown)
            return new DiffSummaries(null, null);

        return new DiffSummaries(
            StatuslineFormatter.ComputeSummary(before),
            StatuslineFormatter.ComputeSummary(after));
    }

    static RegressionGateResult EvaluateRegressionGate(DiffSummaries summaries, bool failOnRegression)
    {
        if (!failOnRegression)
            return new RegressionGateResult(null, 0);

        var gate = DiffGate.EvaluateRegression(summaries.Before!, summaries.After!);
        if (!gate.HasRegression)
            return new RegressionGateResult(gate, 0);

        Console.Error.WriteLine(gate.Reason);
        return new RegressionGateResult(gate, 2);
    }

    static int WriteFormattedOutput(DiffOutputContext ctx)
    {
        if (ctx.Format == OutputFormat.Html)
            return WriteHtmlOutput(ctx);

        if (ctx.Format == OutputFormat.Markdown)
            return WriteMarkdownOutput(ctx);

        return WriteJsonOutput(ctx);
    }

    static int WriteHtmlOutput(DiffOutputContext ctx)
    {
        var htmlPath = ctx.Output ?? Path.Combine(
            Path.GetTempPath(),
            $"unilyze-diff-{Path.GetFileNameWithoutExtension(ctx.BeforePath)}-{Path.GetFileNameWithoutExtension(ctx.AfterPath)}.html");

        var html = HtmlFormatter.GenerateWithDiff(ctx.AfterJson, ctx.DiffJson, ctx.After.ProjectPath);
        File.WriteAllText(htmlPath, html);
        Console.Error.WriteLine($"Written to {htmlPath}");

        if (ctx.Output == null && !ctx.NoOpen)
            ProgramHelpers.TryOpenInBrowser(htmlPath);

        return ResolveExitCode(ctx);
    }

    static int WriteMarkdownOutput(DiffOutputContext ctx)
    {
        var markdown = MarkdownDiffFormatter.Generate(
            ctx.Diff, ctx.Summaries.Before!, ctx.Summaries.After!,
            ctx.FailOnRegression ? ctx.RegressionGate.Gate : null,
            ctx.FailOnDeltaBelow);
        var markdownWrite = ProgramHelpers.WriteOutput(markdown, ctx.Output);
        if (markdownWrite != 0)
            return markdownWrite;
        return ResolveExitCode(ctx);
    }

    static int WriteJsonOutput(DiffOutputContext ctx)
    {
        var jsonOutput = ctx.ChangedOnly
            ? JsonSerializer.Serialize(ctx.Diff with { Unchanged = Array.Empty<TypeDiff>() }, AnalysisJsonContext.Default.DiffResult)
            : ctx.DiffJson;
        var writeResult = ProgramHelpers.WriteOutput(jsonOutput, ctx.Output);
        if (writeResult != 0)
            return writeResult;
        return ResolveExitCode(ctx);
    }

    static int ResolveExitCode(DiffOutputContext ctx) =>
        ctx.RegressionGate.GateExit != 0 || ctx.VersionExit != 0 || ctx.DeltaExit != 0 ? 2 : 0;

    static void PrintSummary(DiffResult diff)
    {
        Console.Error.WriteLine($"Diff: {diff.BeforePath} -> {diff.AfterPath}");
        Console.Error.WriteLine($"  Improved:  {diff.Summary.ImprovedCount}");
        Console.Error.WriteLine($"  Degraded:  {diff.Summary.DegradedCount}");
        Console.Error.WriteLine($"  Unchanged: {diff.Summary.UnchangedCount}");
        Console.Error.WriteLine($"  Added:     {diff.Summary.AddedCount}");
        Console.Error.WriteLine($"  Removed:   {diff.Summary.RemovedCount}");
        Console.Error.WriteLine(
            $"  DeltaScore: {diff.DeltaScore.ToString("0.###", CultureInfo.InvariantCulture)} "
            + $"({diff.LowRiskChangeCount} low / {diff.HighRiskChangeCount} high risk)");

        if (diff.Degraded.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Degraded types:");
            foreach (var t in diff.Degraded)
                Console.Error.WriteLine($"  {t.TypeKey}");
        }

        if (diff.Improved.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Improved types:");
            foreach (var t in diff.Improved)
                Console.Error.WriteLine($"  {t.TypeKey}");
        }
    }

    static int PrintUsage()
    {
        Console.WriteLine("""
            unilyze diff - Compare two analysis snapshots

            Usage:
              unilyze diff <before.json> <after.json>                   Output diff JSON to stdout
              unilyze diff <before.json> <after.json> -o out.json       Save diff JSON to file
              unilyze diff <before.json> <after.json> -o out.html       Render diff as interactive HTML viewer
              unilyze diff <before.json> <after.json> -f html           Render HTML to temp dir and open in browser
              unilyze diff <before.json> <after.json> -f markdown       Output GFM markdown to stdout (CI / PR comments)
              unilyze diff <before.json> <after.json> --fail-on-regression  Exit 2 if quality regressed (CI gate)
              unilyze diff <before.json> <after.json> --fail-on-version-mismatch  Exit 2 if metricsVersion differs
              unilyze diff <before.json> <after.json> --changed-only             Omit unchanged types from JSON output
              unilyze diff <before.json> <after.json> --fail-on-delta-below 0.8 Exit 2 if deltaScore is below 0.8
              unilyze diff --base-ref <git-ref> <after.json>            Analyze base ref in a temp worktree and diff

            Options:
              -o, --output             Output file path (format inferred from extension: .html or .json)
              -f, --format             Output format: json, html, markdown (default: json when no -o specified)
              -p, --path               Project path for base analysis (default: after snapshot projectPath)
                  --base-ref           Git ref for baseline; analyzes it in a temporary worktree (one after.json positional)
                  --level              Pin analysis level for base side: syntax, core, full, complete
                  --no-open            When generating HTML, do not auto-open in browser
                  --fail-on-regression Exit 2 when avg/min CodeHealth dropped or smells (warning/critical) increased
                  --fail-on-version-mismatch Exit 2 when metricsVersion differs between snapshots
                  --changed-only       Omit unchanged types from JSON output (summary counts preserved)
                  --fail-on-delta-below Exit 2 when deltaScore is below the given value (0..1)
              -h, --help               Show this help

            Exit codes:
              0  Success / no regression
              1  Usage error
              2  A configured diff quality gate failed

            CI PR gate (single command):
              git fetch origin main   # or use fetch-depth: 0 in checkout
              unilyze -p . -o after.json
              unilyze diff --base-ref origin/main after.json -f markdown --fail-on-regression
            """);
        return 0;
    }
}
