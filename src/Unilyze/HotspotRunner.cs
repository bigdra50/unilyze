using System.Text.Json;

namespace Unilyze;

internal static class HotspotRunner
{
    public static int Run(string[] args)
    {
        if (ProgramHelpers.IsHelpRequest(args))
            return PrintUsage();

        var usageError = ProgramHelpers.ValidateHotspotArgs(args);
        if (usageError != 0)
            return usageError;

        var opts = ProgramHelpers.ParseOptions(args);
        var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path");
        var input = opts.GetValueOrDefault("-i") ?? opts.GetValueOrDefault("--input");
        var since = opts.GetValueOrDefault("--since") ?? "12.month";
        var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
        var hotspotExcludeDirs = ProgramHelpers.ParseMultiValueOption(args, "--exclude-dir");

        if (!int.TryParse(opts.GetValueOrDefault("-n") ?? "20", out var topN))
            topN = 20;

        path ??= ".";

        try
        {
            IReadOnlyList<TypeMetrics> typeMetrics;
            if (input != null)
            {
                var json = File.ReadAllText(input);
                var result = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.AnalysisResult)
                             ?? throw new InvalidOperationException($"Failed to parse: {input}");
                typeMetrics = result.TypeMetrics ?? [];
            }
            else
            {
                var hotspotRoot = ProgramHelpers.ResolveProjectRoot(path);
                var hotspotConfig = UnilyzeConfig.LoadMerged(hotspotRoot, hotspotExcludeDirs);
                var resolved = hotspotConfig.ResolveAnalysisConfig();
                var result = AnalysisPipeline.Build(
                    path, null, null, hotspotConfig.ExcludeDirs,
                    excludeGeneratedCode: !hotspotConfig.DisableGeneratedCodeExcludes,
                    applyAnyDepthExcludes: !hotspotConfig.DisableDefaultExcludes,
                    thresholds: resolved.Thresholds,
                    disabledRuleKinds: resolved.DisabledRuleKinds,
                    disableCycles: resolved.DisableCycles);
                typeMetrics = result.TypeMetrics ?? [];
            }

            var gitLogOutput = HotspotAnalyzer.RunGitLog(path, since);
            var changeFrequencies = HotspotAnalyzer.ParseGitLog(gitLogOutput);
            var hotspot = HotspotAnalyzer.Analyze(typeMetrics, changeFrequencies, path, since, topN);

            var hotspotJson = JsonSerializer.Serialize(hotspot, AnalysisJsonContext.Default.HotspotResult);

            PrintSummary(hotspot);

            return ProgramHelpers.WriteOutput(hotspotJson, output);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static void PrintSummary(HotspotResult hotspot)
    {
        Console.Error.WriteLine($"Hotspot analysis: {hotspot.ProjectPath} (since {hotspot.Since})");
        Console.Error.WriteLine($"  Total hotspots: {hotspot.Hotspots.Count}");
        Console.Error.WriteLine();

        if (hotspot.Hotspots.Count == 0)
        {
            Console.Error.WriteLine("  No hotspots found.");
            return;
        }

        Console.Error.WriteLine("  Rank  Score   Churn  Health  Type");
        Console.Error.WriteLine("  ----  ------  -----  ------  ----");
        for (var i = 0; i < hotspot.Hotspots.Count; i++)
        {
            var h = hotspot.Hotspots[i];
            var typeName = string.IsNullOrEmpty(h.Namespace) ? h.TypeName : $"{h.Namespace}.{h.TypeName}";
            Console.Error.WriteLine($"  {i + 1,4}  {h.HotspotScore,6:F1}  {h.ChangeCount,5}  {h.CodeHealth,6:F1}  {typeName}");
        }
    }

    static int PrintUsage()
    {
        Console.WriteLine("""
            unilyze hotspot - Identify refactoring hotspots (git churn x complexity)

            Usage:
              unilyze hotspot                                    Analyze current directory
              unilyze hotspot -p <path>                         Analyze specified project
              unilyze hotspot -p <path> -i analysis.json         Use existing analysis JSON
              unilyze hotspot -p <path> --since 6.month -n 10   Custom period and top N
              unilyze hotspot -p <path> -o hotspots.json         Save to file

            Options:
              -p, --path         Project root (default: ., used for git log)
              -i, --input        Existing analysis JSON (skip fresh analysis)
              --since            Git log period (default: 12.month)
              -n                 Top N results (default: 20)
              --exclude-dir      Exclude directory from analysis (repeatable)
              -o, --output       Output file path
              -h, --help         Show this help
            """);
        return 0;
    }
}
