using System.Text.Json;

namespace Unilyze;

internal static class TrendRunner
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || CliArgValidationSupport.IsHelpRequest(args))
            return PrintUsage();

        var usageError = CliArgValidation.ValidateTrendArgs(args);
        if (usageError != 0)
            return usageError;

        var positional = args.Where(a => !a.StartsWith('-')).ToList();
        if (positional.Count < 1)
        {
            Console.Error.WriteLine("Usage: unilyze trend <dir-of-jsons> [-o output.json]");
            return 1;
        }

        var opts = ProgramHelpers.ParseOptions(args);
        var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
        var formatStr = opts.GetValueOrDefault("-f") ?? opts.GetValueOrDefault("--format");
        var noOpen = opts.ContainsKey("--no-open");
        var dir = positional[0];

        if (TryResolveTrendFormat(formatStr, output, out var format) != 0)
            return 1;

        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"Directory not found: {dir}");
            return 1;
        }

        try
        {
            var jsonFiles = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f)
                .ToList();

            if (jsonFiles.Count == 0)
            {
                Console.Error.WriteLine($"No JSON files found in: {dir}");
                return 1;
            }

            var entries = new List<(string? SourceFile, AnalysisResult Result)>();
            foreach (var file in jsonFiles)
            {
                var json = File.ReadAllText(file);
                var result = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.AnalysisResult);
                if (result is null)
                {
                    Console.Error.WriteLine($"Skipping invalid file: {file}");
                    continue;
                }
                entries.Add((Path.GetFileName(file), result));
            }

            if (entries.Count == 0)
            {
                Console.Error.WriteLine("No valid analysis results found.");
                return 1;
            }

            var results = entries.Select(e => e.Result).ToList();

            var distinctMetricsVersions = results.Select(r => r.MetricsVersion).Distinct().ToList();
            if (distinctMetricsVersions.Count > 1)
            {
                var formatted = string.Join(", ", distinctMetricsVersions.Select(ToolVersionInfo.FormatMetricsVersion));
                Console.Error.WriteLine(
                    $"Warning: metrics versions differ across snapshots ({formatted}). Trend deltas may be unreliable.");
            }

            var distinctProfiles = results
                .Select(r => r.Profile ?? SmellThresholdProfiles.DefaultProfileName)
                .Distinct()
                .ToList();
            if (distinctProfiles.Count > 1)
            {
                Console.Error.WriteLine(
                    $"Warning: profiles differ across snapshots ({string.Join(", ", distinctProfiles)}). "
                    + "Trend smell deltas may be unreliable.");
            }

            var trend = TrendAnalyzer.AnalyzeSnapshots(entries);
            var trendJson = JsonSerializer.Serialize(trend, AnalysisJsonContext.Default.TrendResult);

            PrintSummary(trend);

            if (format == OutputFormat.Html)
                return WriteHtmlOutput(trendJson, dir, output, noOpen);

            return ProgramHelpers.WriteOutput(trendJson, output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static int TryResolveTrendFormat(string? formatStr, string? output, out OutputFormat format)
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

        if (format is OutputFormat.Sarif or OutputFormat.Markdown)
        {
            Console.Error.WriteLine("Trend does not support SARIF or Markdown output. Use json or html.");
            return 1;
        }

        if (formatStr == null && output == null)
            format = OutputFormat.Json;

        return 0;
    }

    static int WriteHtmlOutput(string trendJson, string inputDir, string? output, bool noOpen)
    {
        var htmlPath = output ?? Path.Combine(
            Path.GetTempPath(),
            $"unilyze-trend-{Path.GetFileName(inputDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}.html");

        var html = TrendHtmlFormatter.Generate(trendJson, inputDir);
        File.WriteAllText(htmlPath, html);
        Console.Error.WriteLine($"Written to {htmlPath}");

        if (output == null && !noOpen)
            ProgramHelpers.TryOpenInBrowser(htmlPath);

        return 0;
    }

    static void PrintSummary(TrendResult trend)
    {
        Console.Error.WriteLine($"Trend: {trend.Summary.SnapshotCount} snapshot(s)");
        Console.Error.WriteLine($"  CodeHealth delta:  {trend.Summary.CodeHealthDelta:+0.0;-0.0;0.0}");
        Console.Error.WriteLine($"  CodeSmell delta:   {trend.Summary.CodeSmellDelta:+0;-0;0}");

        if (trend.Snapshots.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("  Date                Types  Health  Smells  HighCC  AvgCogCC");
            Console.Error.WriteLine("  ------------------  -----  ------  ------  ------  --------");
            foreach (var s in trend.Snapshots)
            {
                Console.Error.WriteLine(
                    $"  {s.AnalyzedAt:yyyy-MM-dd HH:mm}  {s.TypeCount,5}  {s.AverageCodeHealth,6:F1}  {s.CodeSmellCount,6}  {s.HighComplexityTypeCount,6}  {s.AverageCognitiveComplexity,8:F1}");
            }
        }
    }

    static int PrintUsage()
    {
        Console.WriteLine("""
            unilyze trend - Show quality trend across multiple snapshots

            Usage:
              unilyze trend <dir-of-jsons>                    Output trend JSON to stdout
              unilyze trend <dir-of-jsons> -o out.json        Save trend JSON to file
              unilyze trend <dir-of-jsons> -o trend.html      Save self-contained HTML charts
              unilyze trend <dir-of-jsons> -f html            HTML to temp file (opens browser)

            Options:
              -o, --output    Output file path (.json or .html extension selects format)
              -f, --format    Output format: json (default) or html
              --no-open       Do not open HTML in browser when writing to a temp file
              -h, --help      Show this help
            """);
        return 0;
    }
}
