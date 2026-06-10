using System.Text.Json;

namespace Unilyze;

internal static class DiffRunner
{
    private sealed record DiffRunOptions(
        string BeforePath,
        string AfterPath,
        string? Output,
        OutputFormat Format,
        bool NoOpen,
        bool FailOnRegression,
        bool FailOnVersionMismatch,
        bool ChangedOnly);

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
        RegressionGateResult RegressionGate,
        int VersionExit);

    public static int Run(string[] args)
    {
        if (args.Length == 0 || ProgramHelpers.IsHelpRequest(args))
            return PrintUsage();

        var usageError = ProgramHelpers.ValidateDiffArgs(args);
        if (usageError != 0)
            return usageError;

        var positional = args.Where(a => !a.StartsWith('-')).ToList();
        if (positional.Count < 2)
        {
            Console.Error.WriteLine("Usage: unilyze diff <before.json> <after.json> [-o output.{json,html}] [-f html] [--no-open] [--fail-on-regression] [--changed-only]");
            return 1;
        }

        var opts = ProgramHelpers.ParseOptions(args);
        var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
        var formatStr = opts.GetValueOrDefault("-f") ?? opts.GetValueOrDefault("--format");
        var noOpen = opts.ContainsKey("--no-open");
        var failOnRegression = opts.ContainsKey("--fail-on-regression");
        var failOnVersionMismatch = opts.ContainsKey("--fail-on-version-mismatch");
        var changedOnly = opts.ContainsKey("--changed-only");

        OutputFormat format;
        try { format = ProgramHelpers.ResolveFormat(formatStr, output); }
        catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 1; }

        if (format == OutputFormat.Sarif)
        {
            Console.Error.WriteLine("Diff does not support SARIF output. Use json or html.");
            return 1;
        }

        if (formatStr == null && output == null)
            format = OutputFormat.Json;

        var beforePath = positional[0];
        var afterPath = positional[1];

        try
        {
            return RunComparison(new DiffRunOptions(
                beforePath, afterPath, output, format, noOpen,
                failOnRegression, failOnVersionMismatch, changedOnly));
        }
        catch (Exception ex) when (ex is FileNotFoundException or JsonException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static int RunComparison(DiffRunOptions options)
    {
        var beforeJson = File.ReadAllText(options.BeforePath);
        var afterJson = File.ReadAllText(options.AfterPath);

        var before = JsonSerializer.Deserialize(beforeJson, AnalysisJsonContext.Default.AnalysisResult)
                     ?? throw new InvalidOperationException($"Failed to parse: {options.BeforePath}");
        var after = JsonSerializer.Deserialize(afterJson, AnalysisJsonContext.Default.AnalysisResult)
                    ?? throw new InvalidOperationException($"Failed to parse: {options.AfterPath}");

        WarnIfAnalysisLevelsDiffer(before, after);

        var versionExit = EvaluateVersionMismatch(before, after, options.FailOnVersionMismatch);

        var diff = DiffCalculator.Compare(before, after);
        var diffJson = JsonSerializer.Serialize(diff, AnalysisJsonContext.Default.DiffResult);

        PrintSummary(diff);

        var summaries = ComputeSummariesIfNeeded(before, after, options.FailOnRegression, options.Format);
        var regressionGate = EvaluateRegressionGate(summaries, options.FailOnRegression);

        return WriteFormattedOutput(new DiffOutputContext(
            options.BeforePath,
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
            regressionGate,
            versionExit));
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

        return ctx.RegressionGate.GateExit != 0 ? ctx.RegressionGate.GateExit : ctx.VersionExit;
    }

    static int WriteMarkdownOutput(DiffOutputContext ctx)
    {
        var markdown = MarkdownDiffFormatter.Generate(
            ctx.Diff, ctx.Summaries.Before!, ctx.Summaries.After!,
            ctx.FailOnRegression ? ctx.RegressionGate.Gate : null);
        var markdownWrite = ProgramHelpers.WriteOutput(markdown, ctx.Output);
        if (markdownWrite != 0)
            return markdownWrite;
        if (ctx.RegressionGate.GateExit != 0)
            return ctx.RegressionGate.GateExit;
        return ctx.VersionExit;
    }

    static int WriteJsonOutput(DiffOutputContext ctx)
    {
        var jsonOutput = ctx.ChangedOnly
            ? JsonSerializer.Serialize(ctx.Diff with { Unchanged = Array.Empty<TypeDiff>() }, AnalysisJsonContext.Default.DiffResult)
            : ctx.DiffJson;
        var writeResult = ProgramHelpers.WriteOutput(jsonOutput, ctx.Output);
        if (writeResult != 0)
            return writeResult;
        if (ctx.RegressionGate.GateExit != 0)
            return ctx.RegressionGate.GateExit;
        return ctx.VersionExit;
    }

    static void PrintSummary(DiffResult diff)
    {
        Console.Error.WriteLine($"Diff: {diff.BeforePath} -> {diff.AfterPath}");
        Console.Error.WriteLine($"  Improved:  {diff.Summary.ImprovedCount}");
        Console.Error.WriteLine($"  Degraded:  {diff.Summary.DegradedCount}");
        Console.Error.WriteLine($"  Unchanged: {diff.Summary.UnchangedCount}");
        Console.Error.WriteLine($"  Added:     {diff.Summary.AddedCount}");
        Console.Error.WriteLine($"  Removed:   {diff.Summary.RemovedCount}");

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

            Options:
              -o, --output             Output file path (format inferred from extension: .html or .json)
              -f, --format             Output format: json, html, markdown (default: json when no -o specified)
                  --no-open            When generating HTML, do not auto-open in browser
                  --fail-on-regression Exit 2 when avg/min CodeHealth dropped or smells (warning/critical) increased
                  --fail-on-version-mismatch Exit 2 when metricsVersion differs between snapshots
                  --changed-only       Omit unchanged types from JSON output (summary counts preserved)
              -h, --help               Show this help

            Exit codes:
              0  Success / no regression
              1  Usage error
              2  Regression detected (with --fail-on-regression) or metricsVersion mismatch (with --fail-on-version-mismatch)
            """);
        return 0;
    }
}
