namespace Unilyze;

internal static class BaselineRunner
{
    public static readonly string[] Subcommands = ["create"];

    public static int Run(string[] args)
    {
        if (args.Length == 0 || CliArgValidationSupport.IsHelpRequest(args))
            return PrintUsage();

        var usageError = CliArgValidation.ValidateBaselineArgs(args);
        if (usageError != 0)
            return usageError;

        var subcommand = args[0];
        return subcommand switch
        {
            "create" => Create(args[1..]),
            _ => CliArgValidationSupport.ReportUnknown("subcommand", subcommand, Subcommands),
        };
    }

    static int Create(string[] args)
    {
        var opts = ProgramHelpers.ParseOptions(args);
        var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
        var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
        var levelStr = opts.GetValueOrDefault("--level");

        AnalysisLevel? requestedLevel = null;
        if (levelStr != null)
        {
            if (!AnalysisLevelOption.TryParse(levelStr, out var lvl))
            {
                Console.Error.WriteLine($"Unknown level: '{levelStr}'. Valid levels: syntax, core, full, complete");
                return 1;
            }
            requestedLevel = lvl;
        }

        try
        {
            var projectRoot = ProgramHelpers.ResolveProjectRoot(path);
            var outputPath = output ?? Path.Combine(projectRoot, ".unilyze", "baseline.json");
            if (!Path.IsPathRooted(outputPath))
                outputPath = Path.GetFullPath(Path.Combine(projectRoot, outputPath));

            var config = UnilyzeConfig.LoadMerged(projectRoot);
            var resolved = config.ResolveAnalysisConfig();
            var result = AnalysisPipeline.Build(
                projectRoot, null, null, config.ExcludeDirs, requestedLevel,
                excludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
                applyAnyDepthExcludes: !config.DisableDefaultExcludes,
                analysisConfig: resolved,
                maxParallelism: config.MaxParallelism);

            var baseline = BaselineFile.FromAnalysis(result);
            BaselineFile.Save(outputPath, baseline);
            Console.Error.WriteLine($"Written to {outputPath}");
            Console.Error.WriteLine(
                $"Baseline: {baseline.Fingerprints.Count} fingerprint(s), "
                + $"{baseline.Fingerprints.Sum(entry => entry.Count)} smell occurrence(s).");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static int PrintUsage()
    {
        Console.WriteLine("""
            unilyze baseline - Snapshot current code smells for zero-new-violations workflows

            Usage:
              unilyze baseline create                         Analyze current directory
              unilyze baseline create -p <path>               Analyze specified project
              unilyze baseline create -p <path> -o file.json  Write baseline to a custom path

            Options:
              -p, --path     Project root (default: .)
              -o, --output   Output file (default: <project>/.unilyze/baseline.json)
              --level        Pin analysis level: syntax, core, full, complete
              -h, --help     Show this help

            Exit codes:
              0  Success
              1  Usage error or analysis failure
            """);
        return 0;
    }
}
