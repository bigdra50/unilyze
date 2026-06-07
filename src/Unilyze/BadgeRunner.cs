namespace Unilyze;

internal static class BadgeRunner
{
    public static int Run(string[] args)
    {
        var opts = ProgramHelpers.ParseOptions(args);
        if (opts.ContainsKey("-h") || opts.ContainsKey("--help"))
            return PrintUsage();

        var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
        var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
        var metricStr = opts.GetValueOrDefault("--metric");

        if (!BadgeFormatter.TryParseMetric(metricStr, out var metric))
        {
            Console.Error.WriteLine($"Unknown metric: '{metricStr}'. Valid metrics: codehealth, mi, smells");
            return 1;
        }

        try
        {
            var fullPath = ProgramHelpers.ResolveProjectRoot(path);
            var config = UnilyzeConfig.LoadMerged(fullPath);
            var result = AnalysisPipeline.Build(fullPath, null, null, config.ExcludeDirs);
            var summary = StatuslineFormatter.ComputeSummary(result);
            var json = BadgeFormatter.Serialize(BadgeFormatter.Build(metric, summary));

            if (output != null)
            {
                File.WriteAllText(output, json);
                Console.Error.WriteLine($"Written to {output}");
                return 0;
            }

            Console.Write(json);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static int PrintUsage()
    {
        Console.WriteLine("""
            unilyze badge - Output shields.io endpoint badge JSON

            Usage:
              unilyze badge                                    Analyze current directory
              unilyze badge -p <path>                          Analyze specified project
              unilyze badge -p <path> --metric codehealth      Code health badge (default)
              unilyze badge -p <path> --metric mi              Maintainability index badge
              unilyze badge -p <path> --metric smells          Code smells badge
              unilyze badge -p <path> -o badge.json            Write JSON to file

            Options:
              -p, --path     Project root (default: .)
              -o, --output   Output file path (default: stdout)
              --metric       Badge metric: codehealth, mi, smells (default: codehealth)
              -h, --help     Show this help

            Output format (shields.io endpoint JSON):
              { "schemaVersion": 1, "label": "...", "message": "...", "color": "..." }
            """);
        return 0;
    }
}
