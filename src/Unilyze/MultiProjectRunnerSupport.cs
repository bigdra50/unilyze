namespace Unilyze;

internal static class MultiProjectRunnerSupport
{
    internal const int ExitUsageError = 1;
    internal const int ExitGateFailed = 2;

    internal static string? ValidateCommon(IReadOnlyDictionary<string, string> opts, IReadOnlyList<string> projectGlobs)
    {
        if (projectGlobs.Count == 0)
            return null;

        if (opts.ContainsKey("-p") || opts.ContainsKey("--path"))
            return "--projects cannot be combined with -p/--path.";

        if (opts.ContainsKey("-i") || opts.ContainsKey("--input"))
            return "--projects cannot be combined with -i/--input.";

        return null;
    }

    internal static string? ValidateMatches(IReadOnlyList<string> projectGlobs, string? outputDir)
    {
        var matches = DirectoryGlobMatcher.Expand(projectGlobs);
        if (matches.Count == 0)
            return "No directories matched --projects glob.";
        if (matches.Count > 1 && outputDir is null)
            return "--projects requires -o <dir> when more than one project matches.";
        return null;
    }

    internal static bool TryApplyPostProcessing(
        IReadOnlyDictionary<string, string> opts,
        UnilyzeConfig config,
        string projectRoot,
        ref AnalysisResult result,
        string? baselineOverride = null)
    {
        var baselinePath = baselineOverride ?? ProgramHelpers.ResolveBaselineOption(opts, config);
        if (ProgramHelpers.TryApplyBaseline(result, projectRoot, baselinePath, out result) is 1)
            return false;

        var triagePath = TriageApplication.ResolvePath(opts, config, projectRoot);
        return TriageApplication.TryApply(result, triagePath, out result) is not 1;
    }

    internal static void WriteSummaryIfNeeded(
        string? outputDir,
        string toolVersion,
        IReadOnlyList<MultiProjectSummaryEntry> entries)
    {
        if (outputDir is null)
            return;

        var summaryPath = Path.Combine(outputDir, "summary.json");
        var summaryDoc = new MultiProjectSummaryDocument(toolVersion, entries);
        File.WriteAllText(summaryPath, MultiProjectSummary.Serialize(summaryDoc));
        Console.Error.WriteLine($"Written to {summaryPath}");
    }

    internal static void WriteProjectOutput(string outputDir, string baseName, string extension, string content)
    {
        Directory.CreateDirectory(outputDir);
        var outputFile = Path.Combine(outputDir, $"{baseName}{extension}");
        File.WriteAllText(outputFile, content);
        Console.Error.WriteLine($"Written to {outputFile}");
    }

    internal static int HandleIoException(Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return ExitUsageError;
    }

    internal static string ToolVersion()
        => typeof(TypeAnalyzer).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    internal static int Fail(string? message, int exitCode)
    {
        if (!string.IsNullOrEmpty(message))
            Console.Error.WriteLine(message);
        return exitCode;
    }

    internal static string MetricSlug(BadgeMetric metric) => metric switch
    {
        BadgeMetric.CodeHealth => "codehealth",
        BadgeMetric.Mi => "mi",
        BadgeMetric.Smells => "smells",
        _ => "badge",
    };
}
