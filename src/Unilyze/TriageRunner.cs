namespace Unilyze;

internal static class TriageRunner
{
    public static readonly string[] Subcommands = ["set", "list", "prune"];

    public static int Run(string[] args)
    {
        if (args.Length == 0 || CliArgValidationSupport.IsHelpRequest(args))
            return PrintUsage();

        var usageError = ValidateArgs(args);
        if (usageError != 0)
            return usageError;

        return args[0] switch
        {
            "set" => Set(args[1..]),
            "list" => List(args[1..]),
            "prune" => Prune(args[1..]),
            _ => CliArgValidationSupport.ReportUnknown("subcommand", args[0], Subcommands),
        };
    }

    static int Set(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: unilyze triage set <id> <verdict> [--reason <text>] [--by <author>]");
            return 1;
        }

        var id = args[0];
        var verdict = args[1];
        if (!TriageVerdicts.IsKnown(verdict))
        {
            Console.Error.WriteLine(
                $"Unknown verdict: '{verdict}'. Valid verdicts: {string.Join(", ", TriageVerdicts.All)}");
            return 1;
        }

        var opts = ProgramHelpers.ParseOptions(args[2..]);
        var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
        var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
        var reason = opts.GetValueOrDefault("--reason");
        var by = opts.GetValueOrDefault("--by");

        try
        {
            var projectRoot = ProgramHelpers.ResolveProjectRoot(path);
            var outputPath = output ?? TriageFile.DefaultPath(projectRoot);
            if (!Path.IsPathRooted(outputPath))
                outputPath = Path.GetFullPath(Path.Combine(projectRoot, outputPath));

            var triage = File.Exists(outputPath) ? TriageFile.Load(outputPath) : TriageFile.CreateEmpty();
            var entry = new TriageEntry(id, verdict, reason, by, DateTimeOffset.UtcNow);
            triage = triage.Upsert(entry);
            TriageFile.Save(outputPath, triage);
            Console.Error.WriteLine($"Written to {outputPath}");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static int List(string[] args)
    {
        var opts = ProgramHelpers.ParseOptions(args);
        var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
        var triagePath = opts.GetValueOrDefault("--triage");

        try
        {
            var projectRoot = ProgramHelpers.ResolveProjectRoot(path);
            var resolvedPath = ResolveTriagePathForCommand(projectRoot, triagePath);
            if (!File.Exists(resolvedPath))
            {
                Console.Error.WriteLine($"Triage file not found: {resolvedPath}");
                return 1;
            }

            var triage = TriageFile.Load(resolvedPath);
            var currentIds = CollectCurrentFindingIds(projectRoot, opts.GetValueOrDefault("--level"));
            var staleCount = triage.Entries.Count(entry => !currentIds.Contains(entry.Id));

            foreach (var entry in triage.Entries.OrderBy(e => e.Id, StringComparer.Ordinal))
            {
                var stale = currentIds.Contains(entry.Id) ? "" : " [stale]";
                var reason = string.IsNullOrEmpty(entry.Reason) ? "" : $" reason=\"{entry.Reason}\"";
                var author = string.IsNullOrEmpty(entry.By) ? "" : $" by={entry.By}";
                Console.WriteLine($"{entry.Id} {entry.Verdict}{stale}{reason}{author}");
            }

            if (staleCount > 0)
                Console.Error.WriteLine($"Triage: {staleCount} stale verdict(s).");

            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static int Prune(string[] args)
    {
        var opts = ProgramHelpers.ParseOptions(args);
        var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
        var triagePath = opts.GetValueOrDefault("--triage");

        try
        {
            var projectRoot = ProgramHelpers.ResolveProjectRoot(path);
            var resolvedPath = ResolveTriagePathForCommand(projectRoot, triagePath);
            if (!File.Exists(resolvedPath))
            {
                Console.Error.WriteLine($"Triage file not found: {resolvedPath}");
                return 1;
            }

            var triage = TriageFile.Load(resolvedPath);
            var beforeCount = triage.Entries.Count;
            var currentIds = CollectCurrentFindingIds(projectRoot, opts.GetValueOrDefault("--level"));
            triage = triage.RemoveStale(currentIds);
            TriageFile.Save(resolvedPath, triage);

            var removed = beforeCount - triage.Entries.Count;
            Console.Error.WriteLine($"Pruned {removed} stale verdict(s) from {resolvedPath}");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static string ResolveTriagePathForCommand(string projectRoot, string? triagePath)
        => triagePath is null
            ? TriageFile.DefaultPath(projectRoot)
            : TriageFile.ResolvePath(projectRoot, triagePath);

    static HashSet<string> CollectCurrentFindingIds(string projectRoot, string? levelStr)
    {
        AnalysisLevel? requestedLevel = null;
        if (levelStr != null)
        {
            if (!AnalysisLevelOption.TryParse(levelStr, out var lvl))
                throw new InvalidOperationException($"Unknown level: '{levelStr}'. Valid levels: syntax, core, full, complete");
            requestedLevel = lvl;
        }

        var config = UnilyzeConfig.LoadMerged(projectRoot);
        var referenceSettings = ReferenceAnalysisSettings.LoadMerged(projectRoot);
        var resolved = config.ResolveAnalysisConfig();
        var result = AnalysisPipeline.Build(
            projectRoot, null, null, config.ExcludeDirs, requestedLevel,
            excludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
            applyAnyDepthExcludes: !config.DisableDefaultExcludes,
            analysisConfig: resolved,
            maxParallelism: config.MaxParallelism,
            resolveNuget: referenceSettings.ResolveNuget,
            includeGenerated: referenceSettings.IncludeGenerated,
            targetFramework: referenceSettings.TargetFramework);
        return TriageMatcher.CollectCurrentIds(result);
    }

    static readonly HashSet<string> SetValueOptions = new(StringComparer.Ordinal)
    {
        "-p", "--path", "-o", "--output", "--reason", "--by",
    };

    static readonly HashSet<string> SetBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help",
    };

    static readonly HashSet<string> ListPruneValueOptions = new(StringComparer.Ordinal)
    {
        "-p", "--path", "--triage", "--level",
    };

    static readonly HashSet<string> ListPruneBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help",
    };

    static int ValidateArgs(string[] args)
    {
        if (CliArgValidationSupport.IsHelpRequest(args) || args.Length == 0)
            return 0;

        if (!Subcommands.Contains(args[0]))
            return CliArgValidationSupport.ReportUnknown("subcommand", args[0], Subcommands);

        var valueOptions = args[0] == "set" ? SetValueOptions : ListPruneValueOptions;
        var booleanOptions = args[0] == "set" ? SetBooleanOptions : ListPruneBooleanOptions;
        var unknown = CliArgValidationSupport.FindUnknownOption(args[1..], valueOptions, booleanOptions);
        return unknown is null
            ? 0
            : CliArgValidationSupport.ReportUnknown("option", unknown, valueOptions.Concat(booleanOptions));
    }

    static int PrintUsage()
    {
        Console.WriteLine("""
            unilyze triage - Persist per-finding verdicts for agentic false-positive filtering

            Usage:
              unilyze triage set <id> <verdict> [--reason <text>] [--by <author>]
              unilyze triage list [-p <path>]
              unilyze triage prune [-p <path>]

            Verdicts:
              confirmed, false-positive, wontfix

            Options:
              -p, --path     Project root (default: .)
              -o, --output   Triage file for set (default: <project>/.unilyze/triage.json)
              --triage       Triage file for list/prune (default: <project>/.unilyze/triage.json)
              --reason       Verdict rationale (set only)
              --by           Author metadata, e.g. agent:claude (set only)
              --level        Pin analysis level for list/prune stale detection
              -h, --help     Show this help

            Exit codes:
              0  Success
              1  Usage error or file failure
            """);
        return 0;
    }
}
