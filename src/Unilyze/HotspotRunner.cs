using System.Text.Json;

namespace Unilyze;

internal static class HotspotRunner
{
    public static int Run(string[] args)
    {
        if (CliArgValidationSupport.IsHelpRequest(args))
            return PrintUsage();

        var usageError = CliArgValidation.ValidateHotspotArgs(args);
        if (usageError != 0)
            return usageError;

        var opts = ProgramHelpers.ParseOptions(args);
        var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path");
        var input = opts.GetValueOrDefault("-i") ?? opts.GetValueOrDefault("--input");
        var since = opts.GetValueOrDefault("--since") ?? "12.month";
        var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
        var halfLifeValue = opts.GetValueOrDefault("--half-life");
        var methodsFile = opts.GetValueOrDefault("--methods");
        var hotspotExcludeDirs = ProgramHelpers.ParseMultiValueOption(args, "--exclude-dir");
        var botPatterns = ProgramHelpers.ParseMultiValueOption(args, "--bot-pattern");
        var noBotFilter = opts.ContainsKey("--no-bot-filter");

        if (!int.TryParse(opts.GetValueOrDefault("-n") ?? "20", out var topN))
            topN = 20;

        path ??= ".";

        TimeSpan? halfLife = null;
        if (halfLifeValue is not null)
        {
            if (!HalfLifeParser.TryParse(halfLifeValue, out var parsed, out var error))
            {
                Console.Error.WriteLine(error);
                return 1;
            }

            halfLife = parsed;
        }

        try
        {
            var matcher = BotAuthorMatcher.CreateDefault();
            foreach (var pattern in botPatterns)
                matcher.AddPattern(pattern);

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
                var referenceSettings = ReferenceAnalysisSettings.LoadMerged(hotspotRoot);
                var resolved = hotspotConfig.ResolveAnalysisConfig();
                var result = AnalysisPipeline.Build(
                    path, null, null, hotspotConfig.ExcludeDirs,
                    excludeGeneratedCode: !hotspotConfig.DisableGeneratedCodeExcludes,
                    applyAnyDepthExcludes: !hotspotConfig.DisableDefaultExcludes,
                    analysisConfig: resolved,
                    maxParallelism: hotspotConfig.MaxParallelism,
                    resolveNuget: referenceSettings.ResolveNuget,
                    includeGenerated: referenceSettings.IncludeGenerated,
                    targetFramework: referenceSettings.TargetFramework);
                typeMetrics = result.TypeMetrics ?? [];
            }

            var gitLogOutput = HotspotAnalyzer.RunGitLog(path, since);
            var allCommits = HotspotAnalyzer.ParseCommitLog(gitLogOutput);
            var (includedCommits, botExcluded) = HotspotAnalyzer.ApplyBotFilter(
                allCommits, matcher, !noBotFilter);
            var changeFrequencies = HotspotAnalyzer.AggregateFileChanges(includedCommits, halfLife);

            IReadOnlyList<MethodHotspot>? methodHotspots = null;
            if (methodsFile is not null)
            {
                methodHotspots = HotspotAnalyzer.AnalyzeMethods(
                    path, methodsFile, typeMetrics, since, matcher, !noBotFilter, halfLife);
            }

            var context = new HotspotAnalysisContext(
                path,
                since,
                topN,
                BotFilter: !noBotFilter,
                BotCommitsExcluded: botExcluded,
                HalfLife: halfLifeValue,
                HalfLifeSpan: halfLife);
            var hotspot = HotspotAnalyzer.Analyze(typeMetrics, changeFrequencies, context);

            if (methodHotspots is not null)
            {
                hotspot = hotspot with { MethodHotspots = methodHotspots };
            }

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
        if (hotspot.BotFilter)
            Console.Error.WriteLine($"  Bot commits excluded: {hotspot.BotCommitsExcluded}");
        if (hotspot.HalfLife is not null)
            Console.Error.WriteLine($"  Half-life decay: {hotspot.HalfLife}");
        Console.Error.WriteLine($"  Total hotspots: {hotspot.Hotspots.Count}");
        Console.Error.WriteLine();

        if (hotspot.MethodHotspots is { Count: > 0 })
        {
            Console.Error.WriteLine("  Method hotspots:");
            Console.Error.WriteLine("  Rank  Score   Churn  CogCC  Method");
            Console.Error.WriteLine("  ----  ------  -----  -----  ------");
            for (var i = 0; i < hotspot.MethodHotspots.Count; i++)
            {
                var m = hotspot.MethodHotspots[i];
                var label = string.IsNullOrEmpty(m.Namespace)
                    ? $"{m.TypeName}.{m.MethodName}"
                    : $"{m.Namespace}.{m.TypeName}.{m.MethodName}";
                Console.Error.WriteLine(
                    $"  {i + 1,4}  {m.HotspotScore,6:F1}  {m.ChangeCount,5}  {m.CognitiveComplexity,5}  {label} (L{m.StartLine}-{m.EndLine})");
            }

            Console.Error.WriteLine();
        }

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
              unilyze hotspot -p <path> --methods src/Foo.cs     Method-level X-Ray for one file
              unilyze hotspot -p <path> --half-life 90.day       Time-decay weighting (opt-in)
              unilyze hotspot -p <path> --no-bot-filter          Include bot-authored commits

            Options:
              -p, --path         Project root (default: ., used for git log)
              -i, --input        Existing analysis JSON (skip fresh analysis)
              --since            Git log period (default: 12.month)
              -n                 Top N results (default: 20)
              --exclude-dir      Exclude directory from analysis (repeatable)
              --half-life        Exponential decay half-life (e.g. 90.day, 6.month); opt-in
              --bot-pattern      Additional bot author regex (repeatable)
              --no-bot-filter    Disable bot commit exclusion (default: bots excluded)
              --methods          Method-level hotspot analysis for one .cs file
              -o, --output       Output file path
              -h, --help         Show this help
            """);
        return 0;
    }
}
