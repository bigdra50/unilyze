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
