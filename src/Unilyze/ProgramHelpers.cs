using System.Text.Json;

namespace Unilyze;

internal static class ProgramHelpers
{
    public static readonly string[] TopLevelCommands =
    [
        "diff", "hotspot", "query", "trend", "metrics", "schema", "statusline", "badge", "config",
        "baseline", "calibrate", "triage", "skills", "help", "version",
    ];

    static readonly HashSet<string> AnalyzeValueOptions = new(StringComparer.Ordinal)
    {
        "-p", "--path", "-i", "--input", "-o", "--output", "--prefix", "-a", "--assembly",
        "-f", "--format", "--exclude-dir", "--level", "--baseline", "--profile", "--triage",
    };

    static readonly HashSet<string> AnalyzeBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help", "-v", "--version", "--no-open", "--no-triage",
    };

    static readonly HashSet<string> NoValueOptions = new(StringComparer.Ordinal);

    static readonly HashSet<string> ConfigValueOptions = NoValueOptions;

    static readonly HashSet<string> ConfigBooleanOptions = new(StringComparer.Ordinal)
    {
        "--global", "-h", "--help",
    };

    public static readonly string[] ConfigSubcommands = ["list", "add-exclude-dir", "remove-exclude-dir"];

    static readonly HashSet<string> SkillsBooleanOptions = new(StringComparer.Ordinal)
    {
        "-g", "--global", "--claude", "--codex", "--cursor", "--gemini", "--windsurf",
    };

    public static readonly string[] SkillsSubcommands = ["install", "uninstall", "list"];

    internal static readonly HashSet<string> DiffValueOptions = new(StringComparer.Ordinal)
    {
        "-o", "--output", "-f", "--format", "-p", "--path", "--base-ref", "--level",
    };

    static readonly HashSet<string> DiffBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help", "--no-open", "--fail-on-regression", "--fail-on-version-mismatch", "--changed-only",
    };

    static readonly HashSet<string> HotspotValueOptions = new(StringComparer.Ordinal)
    {
        "-p", "--path", "-i", "--input", "--since", "-n", "-o", "--output", "--exclude-dir",
    };

    static readonly HashSet<string> HotspotBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help",
    };

    static readonly HashSet<string> QueryValueOptions = new(StringComparer.Ordinal)
    {
        "-p", "--path", "-i", "--input", "-o", "--output", "-f", "--format", "--worst", "--type", "--exclude-dir",
    };

    static readonly HashSet<string> QueryBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help",
    };

    static readonly HashSet<string> TrendValueOptions = new(StringComparer.Ordinal)
    {
        "-o", "--output",
    };

    static readonly HashSet<string> TrendBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help",
    };

    static readonly HashSet<string> CalibrateValueOptions = new(StringComparer.Ordinal)
    {
        "-o", "--output",
    };

    static readonly HashSet<string> CalibrateBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help",
    };

    static readonly HashSet<string> StatuslineValueOptions = new(StringComparer.Ordinal)
    {
        "-p", "--path", "--refresh", "--level", "--baseline",
    };

    static readonly HashSet<string> StatuslineBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help", "--verbose", "--quiet", "--background-refresh",
    };

    static readonly HashSet<string> BadgeValueOptions = new(StringComparer.Ordinal)
    {
        "-p", "--path", "-o", "--output", "--metric", "--format", "--level", "--fail-under", "--fail-over",
        "--baseline",
    };

    static readonly HashSet<string> BadgeBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help",
    };

    static readonly HashSet<string> MetricsValueOptions = new(StringComparer.Ordinal)
    {
        "--profile",
    };

    static readonly HashSet<string> MetricsBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help",
    };

    static readonly HashSet<string> SchemaBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help",
    };

    static readonly HashSet<string> BaselineCreateValueOptions = new(StringComparer.Ordinal)
    {
        "-p", "--path", "-o", "--output", "--level",
    };

    static readonly HashSet<string> BaselineCreateBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help",
    };

    public static readonly string[] BaselineSubcommands = ["create"];

    public static bool IsHelpRequest(string[] args) =>
        args.Any(a => a is "-h" or "--help");

    public static int ReportUnknown(string kind, string token, IEnumerable<string> candidates)
    {
        var message = $"Unknown {kind}: '{token}'";
        var suggestion = FindClosestMatch(token, candidates);
        if (suggestion is not null)
            message += $". Did you mean '{suggestion}'?";
        Console.Error.WriteLine(message);
        return 1;
    }

    public static string? FindUnknownOption(
        string[] args,
        IReadOnlySet<string> valueOptions,
        IReadOnlySet<string> booleanOptions)
    {
        var known = new HashSet<string>(valueOptions, StringComparer.Ordinal);
        foreach (var option in booleanOptions)
            known.Add(option);

        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith('-'))
                continue;
            if (!known.Contains(args[i]))
                return args[i];
            if (valueOptions.Contains(args[i]))
                i++;
        }

        return null;
    }

    public static string? FindUnexpectedPositional(
        string[] args,
        IReadOnlySet<string> valueOptions)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith('-'))
                return args[i];
            if (valueOptions.Contains(args[i]))
                i++;
        }

        return null;
    }

    public static List<string> ExtractPositionalArgs(
        string[] args,
        IReadOnlySet<string> valueOptions)
    {
        var positionals = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith('-'))
            {
                positionals.Add(args[i]);
                continue;
            }

            if (valueOptions.Contains(args[i]))
                i++;
        }

        return positionals;
    }

    public static int ValidateAnalyzeOptions(string[] args)
    {
        var unknown = FindUnknownOption(args, AnalyzeValueOptions, AnalyzeBooleanOptions);
        return unknown is null ? 0 : ReportUnknown("option", unknown, AnalyzeValueOptions.Concat(AnalyzeBooleanOptions));
    }

    public static int ValidateConfigArgs(string[] args)
    {
        if (IsHelpRequest(args))
            return 0;

        if (args.Length == 0)
            return 0;

        var subcommand = args[0];
        if (!ConfigSubcommands.Contains(subcommand))
            return ReportUnknown("subcommand", subcommand, ConfigSubcommands);

        var unknown = FindUnknownOption(args[1..], ConfigValueOptions, ConfigBooleanOptions);
        return unknown is null ? 0 : ReportUnknown("option", unknown, ConfigBooleanOptions);
    }

    public static int ValidateSkillsArgs(string[] args)
    {
        if (args.Length < 2)
            return 0;

        if (IsHelpRequest(args))
            return 0;

        var subcommand = args[1];
        if (!SkillsSubcommands.Contains(subcommand))
            return ReportUnknown("subcommand", subcommand, SkillsSubcommands);

        var unknown = FindUnknownOption(args[2..], NoValueOptions, SkillsBooleanOptions);
        return unknown is null ? 0 : ReportUnknown("option", unknown, SkillsBooleanOptions);
    }

    public static int ValidateDiffArgs(string[] args)
    {
        if (IsHelpRequest(args))
            return 0;

        var unknown = FindUnknownOption(args, DiffValueOptions, DiffBooleanOptions);
        return unknown is null ? 0 : ReportUnknown("option", unknown, DiffValueOptions.Concat(DiffBooleanOptions));
    }

    public static int ValidateHotspotArgs(string[] args)
    {
        if (IsHelpRequest(args))
            return 0;

        var unknown = FindUnknownOption(args, HotspotValueOptions, HotspotBooleanOptions);
        if (unknown is not null)
            return ReportUnknown("option", unknown, HotspotValueOptions.Concat(HotspotBooleanOptions));

        var extra = FindUnexpectedPositional(args, HotspotValueOptions);
        return extra is null ? 0 : ReportUnknown("subcommand", extra, ["hotspot"]);
    }

    public static int ValidateQueryArgs(string[] args)
    {
        if (IsHelpRequest(args))
            return 0;

        var unknown = FindUnknownOption(args, QueryValueOptions, QueryBooleanOptions);
        if (unknown is not null)
            return ReportUnknown("option", unknown, QueryValueOptions.Concat(QueryBooleanOptions));

        var extra = FindUnexpectedPositional(args, QueryValueOptions);
        return extra is null ? 0 : ReportUnknown("subcommand", extra, ["query"]);
    }

    public static int ValidateTrendArgs(string[] args)
    {
        if (IsHelpRequest(args))
            return 0;

        var unknown = FindUnknownOption(args, TrendValueOptions, TrendBooleanOptions);
        return unknown is null ? 0 : ReportUnknown("option", unknown, TrendValueOptions.Concat(TrendBooleanOptions));
    }

    public static int ValidateCalibrateArgs(string[] args)
    {
        if (IsHelpRequest(args))
            return 0;

        var unknown = FindUnknownOption(args, CalibrateValueOptions, CalibrateBooleanOptions);
        return unknown is null ? 0 : ReportUnknown("option", unknown, CalibrateValueOptions.Concat(CalibrateBooleanOptions));
    }

    public static int ValidateStatuslineArgs(string[] args)
    {
        if (IsHelpRequest(args))
            return 0;

        var unknown = FindUnknownOption(args, StatuslineValueOptions, StatuslineBooleanOptions);
        if (unknown is not null)
            return ReportUnknown("option", unknown, StatuslineValueOptions.Concat(StatuslineBooleanOptions));

        var extra = FindUnexpectedPositional(args, StatuslineValueOptions);
        return extra is null ? 0 : ReportUnknown("subcommand", extra, ["statusline"]);
    }

    public static int ValidateBadgeArgs(string[] args)
    {
        if (IsHelpRequest(args))
            return 0;

        var unknown = FindUnknownOption(args, BadgeValueOptions, BadgeBooleanOptions);
        if (unknown is not null)
            return ReportUnknown("option", unknown, BadgeValueOptions.Concat(BadgeBooleanOptions));

        var extra = FindUnexpectedPositional(args, BadgeValueOptions);
        return extra is null ? 0 : ReportUnknown("subcommand", extra, ["badge"]);
    }

    public static int ValidateMetricsArgs(string[] args)
    {
        if (IsHelpRequest(args))
            return 0;

        var unknown = FindUnknownOption(args, MetricsValueOptions, MetricsBooleanOptions);
        if (unknown is not null)
            return ReportUnknown("option", unknown, MetricsValueOptions.Concat(MetricsBooleanOptions));

        var extra = FindUnexpectedPositional(args, MetricsValueOptions);
        return extra is null ? 0 : ReportUnknown("subcommand", extra, ["metrics"]);
    }

    public static int ValidateSchemaArgs(string[] args)
    {
        if (IsHelpRequest(args))
            return 0;

        var unknown = FindUnknownOption(args, NoValueOptions, SchemaBooleanOptions);
        if (unknown is not null)
            return ReportUnknown("option", unknown, SchemaBooleanOptions);

        var extra = args.FirstOrDefault(a => !a.StartsWith('-'));
        return extra is null ? 0 : ReportUnknown("subcommand", extra, ["schema"]);
    }

    public static int ValidateBaselineArgs(string[] args)
    {
        if (IsHelpRequest(args))
            return 0;

        if (args.Length == 0)
            return 0;

        var subcommand = args[0];
        if (!BaselineSubcommands.Contains(subcommand))
            return ReportUnknown("subcommand", subcommand, BaselineSubcommands);

        var unknown = FindUnknownOption(args[1..], BaselineCreateValueOptions, BaselineCreateBooleanOptions);
        return unknown is null ? 0 : ReportUnknown("option", unknown, BaselineCreateValueOptions.Concat(BaselineCreateBooleanOptions));
    }

    public static int ValidateTopLevelCommand(string command)
    {
        if (TopLevelCommands.Contains(command))
            return 0;
        return ReportUnknown("subcommand", command, TopLevelCommands.Where(c => c is not "help" and not "version"));
    }

    public static string? FindClosestMatch(string token, IEnumerable<string> candidates, int maxDistance = 2)
    {
        string? best = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            var distance = LevenshteinDistance(token, candidate);
            if (distance > maxDistance || distance >= bestDistance)
                continue;
            bestDistance = distance;
            best = candidate;
        }

        return best;
    }

    public static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    public static Dictionary<string, string> ParseOptions(string[] args)
    {
        var opts = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith('-'))
            {
                if (args[i] is "-h" or "--help" or "-v" or "--version" or "--no-open" or "--no-triage"
                    or "--fail-on-regression" or "--fail-on-version-mismatch" or "--changed-only"
                    or "--verbose" or "--quiet" or "--background-refresh")
                    opts[args[i]] = "true";
                else if (i + 1 < args.Length)
                {
                    opts[args[i]] = args[i + 1];
                    i++;
                }
            }
        }
        return opts;
    }

    /// <summary>
    /// True when <paramref name="flag"/> appears in <paramref name="args"/> but
    /// has no value token after it (last position, or immediately followed by
    /// another option). ParseOptions silently drops such flags, which would turn
    /// a value-taking gate flag into a no-op (false green in CI). Callers use
    /// this to surface a usage error instead.
    /// </summary>
    public static bool HasFlagWithoutValue(string[] args, string flag)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] != flag)
                continue;
            if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                return true;
        }
        return false;
    }

    public static int? TryApplyBaseline(
        AnalysisResult result,
        string projectRoot,
        string? baselinePath,
        out AnalysisResult updated)
    {
        updated = result;
        if (baselinePath is null)
            return null;

        var resolvedPath = BaselineFile.ResolvePath(projectRoot, baselinePath);
        if (!File.Exists(resolvedPath))
        {
            Console.Error.WriteLine($"Baseline file not found: {resolvedPath}");
            return 1;
        }

        try
        {
            var baseline = BaselineFile.Load(resolvedPath);
            BaselineMatcher.WarnIfMetricsVersionMismatch(baseline);
            updated = BaselineMatcher.Apply(result, baseline, out var stats);
            BaselineMatcher.WriteSummary(stats);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    public static string? ResolveBaselineOption(
        IReadOnlyDictionary<string, string> opts,
        UnilyzeConfig config)
        => opts.GetValueOrDefault("--baseline") ?? config.Baseline;

    public static OutputFormat ResolveFormat(string? formatStr, string? output)
    {
        if (formatStr != null)
        {
            return formatStr.ToLowerInvariant() switch
            {
                "json" => OutputFormat.Json,
                "html" => OutputFormat.Html,
                "sarif" => OutputFormat.Sarif,
                "markdown" => OutputFormat.Markdown,
                _ => throw new ArgumentException($"Unknown format: '{formatStr}'. Valid formats: json, html, sarif, markdown")
            };
        }

        if (output != null)
        {
            return Path.GetExtension(output).ToLowerInvariant() switch
            {
                ".html" or ".htm" => OutputFormat.Html,
                ".json" => OutputFormat.Json,
                ".sarif" => OutputFormat.Sarif,
                _ => OutputFormat.Json
            };
        }

        return OutputFormat.Html;
    }

    public static IReadOnlyList<AsmdefInfo> FilterAssemblies(
        IReadOnlyList<AsmdefInfo> asmdefs, string? prefix, string? assemblyFilter)
    {
        if (assemblyFilter != null)
        {
            var filtered = asmdefs.Where(a =>
                a.Name.Equals(assemblyFilter, StringComparison.OrdinalIgnoreCase)
                || a.Name.EndsWith("." + assemblyFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (filtered.Count == 0)
                throw new InvalidOperationException($"Assembly '{assemblyFilter}' not found.");
            return filtered;
        }

        if (prefix != null)
            return asmdefs.Where(a => a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

        return asmdefs.ToList();
    }

    public static string? DetectCommonPrefix(IReadOnlyList<AsmdefInfo> asmdefs)
    {
        var names = asmdefs.Select(a => a.Name).ToList();
        if (names.Count < 2) return null;
        var parts = names[0].Split('.');
        for (var len = parts.Length; len > 0; len--)
        {
            var candidate = string.Join(".", parts.Take(len)) + ".";
            if (names.All(n => n.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
        return null;
    }

    public static IReadOnlyList<string> ParseMultiValueOption(string[] args, string key)
    {
        var values = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == key)
            {
                values.Add(args[i + 1]);
                i++;
            }
        }
        return values;
    }

    public static string ResolveAssetsDir(string path)
    {
        if (Directory.Exists(Path.Combine(path, "Assets")))
            return Path.Combine(path, "Assets");
        if (Path.GetFileName(path) == "Assets")
            return path;
        return path;
    }

    public static string ResolveProjectRoot(string path)
    {
        var dir = Path.GetFullPath(path);
        for (var i = 0; i < 5; i++)
        {
            if (File.Exists(Path.Combine(dir, "ProjectSettings", "ProjectVersion.txt")))
                return dir;
            if (Directory.EnumerateFiles(dir, "*.sln", SearchOption.TopDirectoryOnly).Any())
                return dir;
            if (Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).Any())
                return dir;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent is null || parent == dir) break;
            dir = parent;
        }

        return Path.GetFullPath(path);
    }

    public static string ResolveProjectKind(string projectRoot)
    {
        if (File.Exists(Path.Combine(projectRoot, "ProjectSettings", "ProjectVersion.txt")))
            return "unity";

        if (CsprojParser.DiscoverCsprojFiles(projectRoot).Count > 0)
            return "dotnet";

        if (Directory.EnumerateFiles(projectRoot, "*.sln", SearchOption.AllDirectories).Any())
            return "dotnet";

        return "unknown";
    }

    public static int WriteOutput(string content, string? outputPath)
    {
        if (outputPath != null)
        {
            File.WriteAllText(outputPath, content);
            Console.Error.WriteLine($"Written to {outputPath}");
            return 0;
        }
        Console.Write(content);
        return 0;
    }

    public static void TryOpenInBrowser(string path)
    {
        try
        {
            var url = "file://" + Path.GetFullPath(path);
            if (OperatingSystem.IsMacOS())
                System.Diagnostics.Process.Start("open", url)?.Dispose();
            else if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            else if (OperatingSystem.IsLinux())
                System.Diagnostics.Process.Start("xdg-open", url)?.Dispose();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to open browser automatically: {ex.Message}");
        }
    }
}
