using System.Text.Json;

namespace Unilyze;

internal static class QueryRunner
{
    public static int Run(string[] args)
    {
        if (ProgramHelpers.IsHelpRequest(args))
            return PrintUsage();

        var usageError = ProgramHelpers.ValidateQueryArgs(args);
        if (usageError != 0)
            return usageError;

        var opts = ProgramHelpers.ParseOptions(args);
        var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path");
        var input = opts.GetValueOrDefault("-i") ?? opts.GetValueOrDefault("--input");
        var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
        var formatStr = opts.GetValueOrDefault("-f") ?? opts.GetValueOrDefault("--format") ?? "md";
        var typeName = opts.GetValueOrDefault("--type");
        var queryExcludeDirs = ProgramHelpers.ParseMultiValueOption(args, "--exclude-dir");

        if (!int.TryParse(opts.GetValueOrDefault("--worst") ?? "5", out var worstCount) || worstCount < 1)
        {
            Console.Error.WriteLine("--worst requires a positive integer");
            return 1;
        }

        path ??= ".";

        try
        {
            var analysis = LoadAnalysis(input, path, queryExcludeDirs);
            var metrics = analysis.TypeMetrics ?? [];

            var selection = typeName != null
                ? QuerySelector.SelectByName(metrics, typeName)
                : QuerySelector.SelectWorst(metrics, worstCount);

            if (selection.AmbiguityMessage != null)
            {
                Console.Error.WriteLine(selection.AmbiguityMessage);
                return 1;
            }

            var queryResult = QueryEvidenceAssembler.Build(analysis, selection.Types);
            var content = formatStr.ToLowerInvariant() switch
            {
                "md" or "markdown" => QueryEvidenceFormatter.ToMarkdown(queryResult),
                "json" => QueryEvidenceFormatter.ToJson(queryResult),
                _ => throw new ArgumentException($"Unknown format: '{formatStr}'. Valid formats: md, json")
            };

            PrintSummary(queryResult, typeName != null ? "type" : "worst", worstCount);
            return ProgramHelpers.WriteOutput(content, output);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static AnalysisResult LoadAnalysis(string? input, string path, IReadOnlyList<string> excludeDirs)
    {
        if (input != null)
        {
            var json = File.ReadAllText(input);
            return JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.AnalysisResult)
                   ?? throw new InvalidOperationException($"Failed to parse: {input}");
        }

        var projectRoot = ProgramHelpers.ResolveProjectRoot(path);
        var config = UnilyzeConfig.LoadMerged(projectRoot, excludeDirs);
        var resolved = config.ResolveAnalysisConfig();
        return AnalysisPipeline.Build(
            path, null, null, config.ExcludeDirs,
            excludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
            applyAnyDepthExcludes: !config.DisableDefaultExcludes,
            thresholds: resolved.Thresholds,
            disabledRuleKinds: resolved.DisabledRuleKinds,
            disableCycles: resolved.DisableCycles);
    }

    static void PrintSummary(QueryResult result, string mode, int worstCount)
    {
        Console.Error.WriteLine($"Query evidence pack: {result.ProjectPath} ({result.Types.Count} types, mode={mode})");
        if (mode == "worst")
            Console.Error.WriteLine($"  Selection: --worst {worstCount}");
    }

    static int PrintUsage()
    {
        Console.WriteLine("""
            unilyze query - Emit per-type evidence packs for agent grounding

            Usage:
              unilyze query --worst 5 -i snapshot.json          Worst types from snapshot
              unilyze query --type SemanticEnricher -i snap.json  Single type by name
              unilyze query --worst 3 -p .                        Analyze project directly
              unilyze query --type Foo -p . -f json -o pack.json  JSON output to file

            Options:
              --worst            Select N lowest CodeHealth types (default: 5)
              --type             Select one type by simple or qualified name
              -p, --path         Project root for direct analysis (default: .)
              -i, --input        Existing analysis JSON (skip fresh analysis)
              -f, --format       Output format: md, json (default: md)
              --exclude-dir      Exclude directory from analysis (repeatable)
              -o, --output       Output file path
              -h, --help         Show this help
            """);
        return 0;
    }
}
