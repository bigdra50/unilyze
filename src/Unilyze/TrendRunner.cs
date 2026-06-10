using System.Text.Json;

namespace Unilyze;

internal static class TrendRunner
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || ProgramHelpers.IsHelpRequest(args))
            return PrintUsage();

        var usageError = ProgramHelpers.ValidateTrendArgs(args);
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
        var dir = positional[0];

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

            var results = new List<AnalysisResult>();
            foreach (var file in jsonFiles)
            {
                var json = File.ReadAllText(file);
                var result = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.AnalysisResult);
                if (result is null)
                {
                    Console.Error.WriteLine($"Skipping invalid file: {file}");
                    continue;
                }
                results.Add(result);
            }

            if (results.Count == 0)
            {
                Console.Error.WriteLine("No valid analysis results found.");
                return 1;
            }

            var distinctMetricsVersions = results.Select(r => r.MetricsVersion).Distinct().ToList();
            if (distinctMetricsVersions.Count > 1)
            {
                var formatted = string.Join(", ", distinctMetricsVersions.Select(ToolVersionInfo.FormatMetricsVersion));
                Console.Error.WriteLine(
                    $"Warning: metrics versions differ across snapshots ({formatted}). Trend deltas may be unreliable.");
            }

            var trend = TrendAnalyzer.Analyze(results);
            var trendJson = JsonSerializer.Serialize(trend, AnalysisJsonContext.Default.TrendResult);

            PrintSummary(trend);

            return ProgramHelpers.WriteOutput(trendJson, output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
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
              unilyze trend <dir-of-jsons>              Output trend JSON to stdout
              unilyze trend <dir-of-jsons> -o out.json   Save trend to file

            Options:
              -o, --output    Output file path
              -h, --help      Show this help
            """);
        return 0;
    }
}
