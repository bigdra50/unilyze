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
        var formatStr = opts.GetValueOrDefault("--format");
        var levelStr = opts.GetValueOrDefault("--level");

        if (!BadgeFormatter.TryParseMetric(metricStr, out var metric))
        {
            Console.Error.WriteLine($"Unknown metric: '{metricStr}'. Valid metrics: codehealth, mi, smells");
            return 1;
        }

        if (!BadgeFormatter.TryParseFormat(formatStr, out var format))
        {
            Console.Error.WriteLine($"Unknown format: '{formatStr}'. Valid formats: json, svg");
            return 1;
        }

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
            var fullPath = ProgramHelpers.ResolveProjectRoot(path);
            var config = UnilyzeConfig.LoadMerged(fullPath);
            var result = AnalysisPipeline.Build(fullPath, null, null, config.ExcludeDirs, requestedLevel);
            var summary = StatuslineFormatter.ComputeSummary(result);
            var badge = BadgeFormatter.Build(metric, summary);
            var content = format == BadgeFormat.Svg ? BadgeSvgRenderer.Render(badge) : BadgeFormatter.Serialize(badge);

            if (output != null)
            {
                File.WriteAllText(output, content);
                Console.Error.WriteLine($"Written to {output}");
                return 0;
            }

            Console.Write(content);
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
            unilyze badge - Output shields.io endpoint badge JSON

            Usage:
              unilyze badge                                    Analyze current directory
              unilyze badge -p <path>                          Analyze specified project
              unilyze badge -p <path> --metric codehealth      Code health badge (default)
              unilyze badge -p <path> --metric mi              Maintainability index badge
              unilyze badge -p <path> --metric smells          Code smells badge
              unilyze badge -p <path> -o badge.json            Write JSON to file
              unilyze badge -p <path> --format svg -o codehealth.svg   SVG badge (works in private repos via relative path)

            Options:
              -p, --path     Project root (default: .)
              -o, --output   Output file path (default: stdout)
              --metric       Badge metric: codehealth, mi, smells (default: codehealth)
              --format       Output format: json, svg (default: json)
              --level        Pin analysis level: syntax, core, full, complete
              -h, --help     Show this help

            Output format (shields.io endpoint JSON or flat SVG):
              { "schemaVersion": 1, "label": "...", "message": "...", "color": "...", "analysisLevel": "..." }
              shields.io ignores the extra analysisLevel field.
              SVG: single-line shields.io flat badge (use --format svg);
                   the analysis level is embedded as a leading XML comment.
            """);
        return 0;
    }
}
