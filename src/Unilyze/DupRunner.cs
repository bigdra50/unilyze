using System.Globalization;

namespace Unilyze;

internal static class DupRunner
{
    public static int Run(string[] args)
    {
        if (ProgramHelpers.IsHelpRequest(args))
            return PrintUsage();

        var usageError = ProgramHelpers.ValidateDupArgs(args);
        if (usageError != 0)
            return usageError;

        var opts = ProgramHelpers.ParseOptions(args);
        var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
        var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
        var formatStr = opts.GetValueOrDefault("-f") ?? opts.GetValueOrDefault("--format");
        var minTokensStr = opts.GetValueOrDefault("--min-tokens");
        var includeThirdParty = opts.ContainsKey("--include-third-party");
        var excludeDirs = ProgramHelpers.ParseMultiValueOption(args, "--exclude-dir");
        var thirdPartyDirs = ProgramHelpers.ParseMultiValueOption(args, "--third-party-dir");

        if (!TryResolveFormat(formatStr, out var format))
        {
            Console.Error.WriteLine($"Unknown format: '{formatStr}'. Valid formats: markdown, text, json");
            return 1;
        }

        try
        {
            var projectRoot = ProgramHelpers.ResolveProjectRoot(path);
            var config = UnilyzeConfig.LoadMerged(projectRoot, excludeDirs);
            var minTokens = DupAnalyzer.ResolveMinTokens(config, minTokensStr);
            if (minTokensStr is not null && minTokens <= 0)
            {
                Console.Error.WriteLine("--min-tokens requires a positive integer.");
                return 1;
            }

            var report = DupAnalyzer.Analyze(new DupAnalysisOptions(
                path,
                minTokens,
                DupAnalyzer.ResolveThirdPartyDirs(projectRoot, config, thirdPartyDirs),
                includeThirdParty,
                ExcludeDirectories: config.ExcludeDirs,
                ExcludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
                ApplyAnyDepthExcludes: !config.DisableDefaultExcludes,
                MaxParallelism: config.MaxParallelism));

            PrintSummary(report);

            var content = format == DupOutputFormat.Json
                ? DupFormatter.FormatJson(report)
                : DupFormatter.FormatMarkdown(report);

            return ProgramHelpers.WriteOutput(content, output);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static void PrintSummary(CloneReport report)
    {
        var summary = report.Summary;
        Console.Error.WriteLine($"Clone detection: {report.ProjectPath} (min {summary.MinTokens} tokens)");
        Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  Duplication: {0:F1}% ({1}/{2} lines)",
            summary.DuplicationPercent, summary.DuplicatedLines, summary.TotalLines));
        Console.Error.WriteLine($"  Clone classes: {summary.CloneClassCount}, suppressed pairs: {summary.SuppressedPairCount}");
        Console.Error.WriteLine();
    }

    static bool TryResolveFormat(string? formatStr, out DupOutputFormat format)
    {
        format = DupOutputFormat.Markdown;
        if (string.IsNullOrWhiteSpace(formatStr))
            return true;

        if (formatStr.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            format = DupOutputFormat.Json;
            return true;
        }

        if (formatStr is "markdown" or "text")
        {
            format = DupOutputFormat.Markdown;
            return true;
        }

        return false;
    }

    static int PrintUsage()
    {
        Console.WriteLine("""
            unilyze dup - Detect duplicated code via normalized token matching

            Usage:
              unilyze dup                                    Analyze current directory
              unilyze dup -p <path>                          Analyze specified project
              unilyze dup -p <path> -f json                  JSON report
              unilyze dup -p <path> --min-tokens 50            Custom minimum window
              unilyze dup -p <path> --include-third-party      Report same-third-party clones

            Options:
              -p, --path              Project root (default: .)
              --min-tokens            Minimum normalized token window (default: 100)
              -f, --format            Output format: markdown (default), text, json
              -o, --output            Output file path
              --exclude-dir           Exclude directory from analysis (repeatable)
              --third-party-dir       Additional third-party root (repeatable)
              --include-third-party   Disable same-third-party pair suppression
              -h, --help              Show this help
            """);
        return 0;
    }
}

internal enum DupOutputFormat
{
    Markdown,
    Json
}
