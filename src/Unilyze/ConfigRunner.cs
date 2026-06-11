namespace Unilyze;

internal static class ConfigRunner
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || ProgramHelpers.IsHelpRequest(args))
            return PrintUsage();

        var usageError = ProgramHelpers.ValidateConfigArgs(args);
        if (usageError != 0)
            return usageError;

        var subcommand = args[0];
        var isGlobal = args.Contains("--global");
        var positional = args.Where(a => !a.StartsWith('-')).Skip(1).ToList();

        var projectRoot = ProgramHelpers.ResolveProjectRoot(".");
        var configPath = isGlobal
            ? UnilyzeConfig.GetGlobalConfigPath()
            : UnilyzeConfig.GetProjectConfigPath(projectRoot);

        return subcommand switch
        {
            "list" => List(projectRoot),
            "add-exclude-dir" => AddExcludeDir(configPath, positional),
            "remove-exclude-dir" => RemoveExcludeDir(configPath, positional),
            _ => ProgramHelpers.ReportUnknown("subcommand", subcommand, ProgramHelpers.ConfigSubcommands),
        };
    }

    static int AddExcludeDir(string configPath, IReadOnlyList<string> positional)
    {
        if (positional.Count == 0)
        {
            Console.Error.WriteLine("Usage: unilyze config add-exclude-dir <dir> [--global]");
            return 1;
        }
        if (UnilyzeConfig.AddExcludeDir(configPath, positional[0]))
        {
            Console.Error.WriteLine($"Added '{positional[0]}' to {configPath}");
            return 0;
        }
        Console.Error.WriteLine($"'{positional[0]}' already exists in {configPath}");
        return 0;
    }

    static int RemoveExcludeDir(string configPath, IReadOnlyList<string> positional)
    {
        if (positional.Count == 0)
        {
            Console.Error.WriteLine("Usage: unilyze config remove-exclude-dir <dir> [--global]");
            return 1;
        }
        if (UnilyzeConfig.RemoveExcludeDir(configPath, positional[0]))
        {
            Console.Error.WriteLine($"Removed '{positional[0]}' from {configPath}");
            return 0;
        }
        Console.Error.WriteLine($"'{positional[0]}' not found in {configPath}");
        return 0;
    }

    static int List(string projectRoot)
    {
        var globalPath = UnilyzeConfig.GetGlobalConfigPath();
        var projectPath = UnilyzeConfig.GetProjectConfigPath(projectRoot);

        var global = UnilyzeConfig.LoadFile(globalPath);
        var project = UnilyzeConfig.LoadFile(projectPath);
        var merged = UnilyzeConfig.LoadMerged(projectRoot);

        var hasAny = false;

        PrintSection("global", globalPath, global, ref hasAny);
        PrintSection("project", projectPath, project, ref hasAny);

        hasAny |= merged.Smells is { Count: > 0 } || merged.Rules is { Count: > 0 };

        if (hasAny)
        {
            Console.WriteLine();
            Console.WriteLine("[effective]");
            Console.WriteLine($"  disableDefaultExcludes: {merged.DisableDefaultExcludes.ToString().ToLowerInvariant()}");
            Console.WriteLine($"  disableGeneratedCodeExcludes: {merged.DisableGeneratedCodeExcludes.ToString().ToLowerInvariant()}");
            if (merged.ExcludeDirs is { Count: > 0 })
            {
                Console.WriteLine("  excludeDirs:");
                foreach (var dir in merged.ExcludeDirs)
                    Console.WriteLine($"    {dir}");
            }

            var effectiveThresholds = EffectiveSmellThresholds.FromOverrides(merged.Smells);
            PrintEffectiveThresholds(effectiveThresholds);
            PrintEffectiveRules(merged.ResolveAnalysisConfig());
        }

        if (!hasAny)
            Console.WriteLine("No configuration found.");

        return 0;
    }

    static void PrintSection(string label, string path, UnilyzeConfig config, ref bool hasAny)
    {
        var sectionHasValues = config.ExcludeDirs is { Count: > 0 }
            || config.DisableDefaultExcludes
            || config.DisableGeneratedCodeExcludes;
        if (!sectionHasValues)
            return;

        if (hasAny) Console.WriteLine();
        hasAny = true;
        Console.WriteLine($"[{label}] {path}");

        if (config.ExcludeDirs is { Count: > 0 })
        {
            Console.WriteLine("  excludeDirs:");
            foreach (var dir in config.ExcludeDirs)
                Console.WriteLine($"    {dir}");
        }

        Console.WriteLine($"  disableDefaultExcludes: {config.DisableDefaultExcludes.ToString().ToLowerInvariant()}");
        Console.WriteLine($"  disableGeneratedCodeExcludes: {config.DisableGeneratedCodeExcludes.ToString().ToLowerInvariant()}");
    }

    static void PrintEffectiveThresholds(EffectiveSmellThresholds thresholds)
    {
        Console.WriteLine("  smells:");
        foreach (var (key, value, overridden) in EffectiveSmellThresholds.BuildConfigEntries(thresholds))
            Console.WriteLine($"    {key}: {value}{(overridden ? "*" : "")}");
    }

    static void PrintEffectiveRules(ResolvedAnalysisConfig resolved)
    {
        Console.WriteLine("  rules:");
        Console.WriteLine($"    UNI001: {RuleState("UNI001", resolved)}");
        Console.WriteLine($"    UNI002: {RuleState("UNI002", resolved)}");
        Console.WriteLine($"    UNI003: {RuleState("UNI003", resolved)}");
        Console.WriteLine($"    UNI004: {RuleState("UNI004", resolved)}");
        Console.WriteLine($"    UNI005: {RuleState("UNI005", resolved)}");
        Console.WriteLine($"    UNI006: {RuleState("UNI006", resolved)}");
        Console.WriteLine($"    UNI007: {RuleState("UNI007", resolved)}");
        Console.WriteLine($"    UNI008: {RuleState("UNI008", resolved)}");
        Console.WriteLine($"    UNI009: {RuleState("UNI009", resolved)}");
        Console.WriteLine($"    UNI010: {RuleState("UNI010", resolved)}");
        Console.WriteLine($"    UNI011: {RuleState("UNI011", resolved)}");
        Console.WriteLine($"    UNI012: {RuleState("UNI012", resolved)}");
        Console.WriteLine($"    UNI013: {RuleState("UNI013", resolved)}");
        Console.WriteLine($"    UNI014: {RuleState("UNI014", resolved)}");
        Console.WriteLine($"    UNI015: {RuleState("UNI015", resolved)}");
        Console.WriteLine($"    UNI016: {RuleState("UNI016", resolved)}");
    }

    static string RuleState(string ruleId, ResolvedAnalysisConfig resolved)
    {
        if (string.Equals(ruleId, "UNI009", StringComparison.OrdinalIgnoreCase))
            return resolved.DisableCycles ? "off" : "on";

        return SarifFormatter.TryGetKind(ruleId, out var kind) && resolved.DisabledRuleKinds.Contains(kind)
            ? "off"
            : "on";
    }

    static int PrintUsage()
    {
        Console.WriteLine("""
            unilyze config - Manage configuration

            Usage:
              unilyze config list                                 Show current configuration
              unilyze config add-exclude-dir <dir>                Add directory to project config
              unilyze config add-exclude-dir <dir> --global       Add directory to global config
              unilyze config remove-exclude-dir <dir>             Remove directory from project config
              unilyze config remove-exclude-dir <dir> --global    Remove directory from global config

            Options:
              --global    Target global config ($XDG_CONFIG_HOME/unilyze/config.json)
              -h, --help  Show this help

            Config files:
              Project: <project-root>/.unilyze.json
              Global:  $XDG_CONFIG_HOME/unilyze/config.json (default: ~/.config/unilyze/config.json)
            """);
        return 0;
    }
}
