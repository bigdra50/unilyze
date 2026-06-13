namespace Unilyze.Cli;

internal static class CliArgValidation
{
    public static readonly string[] TopLevelCommands =
    [
        "diff", "hotspot", "dup", "query", "trend", "metrics", "schema", "statusline", "badge", "config",
        "baseline", "calibrate", "triage", "skills", "help", "version",
        "diff", "hotspot", "query", "trend", "metrics", "schema", "statusline", "badge", "config",
        "baseline", "calibrate", "triage", "mcp", "skills", "help", "version",
    ];

    static readonly HashSet<string> AnalyzeValueOptions = new(StringComparer.Ordinal)
    {
        "-p", "--path", "-i", "--input", "-o", "--output", "--prefix", "-a", "--assembly",
        "-f", "--format", "--exclude-dir", "--level", "--baseline", "--profile", "--triage", "--projects", "--tfm",
    };

    static readonly HashSet<string> AnalyzeBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help", "-v", "--version", "--no-open", "--no-triage", "--include-api-surface",
        "--resolve-nuget", "--include-generated", "--incremental",
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
        "--fail-on-delta-below",
    };

    static readonly HashSet<string> DiffBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help", "--no-open", "--fail-on-regression", "--fail-on-version-mismatch", "--changed-only",
        "--codehealth-v1",
    };

    static readonly HashSet<string> HotspotValueOptions = new(StringComparer.Ordinal)
    {
        "-p", "--path", "-i", "--input", "--since", "-n", "-o", "--output", "--exclude-dir",
        "--half-life", "--bot-pattern", "--methods",
    };

    static readonly HashSet<string> HotspotBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help", "--no-bot-filter",
    };

    static readonly HashSet<string> DupValueOptions = new(StringComparer.Ordinal)
    {
        "-p", "--path", "-o", "--output", "-f", "--format", "--min-tokens", "--exclude-dir", "--third-party-dir",
    };

    static readonly HashSet<string> DupBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help", "--include-third-party",
    };

    static readonly HashSet<string> QueryValueOptions = new(StringComparer.Ordinal)
    {
        "-p", "--path", "-i", "--input", "-o", "--output", "-f", "--format", "--worst", "--type", "--exclude-dir",
    };

    static readonly HashSet<string> QueryBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help", "--include-api-surface",
    };

    static readonly HashSet<string> TrendValueOptions = new(StringComparer.Ordinal)
    {
        "-o", "--output", "-f", "--format",
    };

    static readonly HashSet<string> TrendBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help", "--no-open",
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
        "-h", "--help", "--verbose", "--quiet", "--background-refresh", "--incremental", "--codehealth-v1",
        "--show-mi",
    };

    static readonly HashSet<string> BadgeValueOptions = new(StringComparer.Ordinal)
    {
        "-p", "--path", "-o", "--output", "--metric", "--format", "--level", "--fail-under", "--fail-over",
        "--baseline", "--projects",
    };

    static readonly HashSet<string> BadgeBooleanOptions = new(StringComparer.Ordinal)
    {
        "-h", "--help", "--codehealth-v1",
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

    static readonly HashSet<string> McpBooleanOptions = new(StringComparer.Ordinal)
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

    public static int ValidateAnalyzeOptions(string[] args) =>
        CliArgValidationSupport.ValidateOptions(args, AnalyzeValueOptions, AnalyzeBooleanOptions);

    public static int ValidateConfigArgs(string[] args)
    {
        if (CliArgValidationSupport.IsHelpRequest(args) || args.Length == 0)
            return 0;

        var subcommandError = CliArgValidationSupport.ValidateSubcommand(args[0], ConfigSubcommands);
        return subcommandError != 0
            ? subcommandError
            : CliArgValidationSupport.ValidateOptions(args[1..], ConfigValueOptions, ConfigBooleanOptions);
    }

    public static int ValidateSkillsArgs(string[] args)
    {
        if (args.Length < 2 || CliArgValidationSupport.IsHelpRequest(args))
            return 0;

        var subcommandError = CliArgValidationSupport.ValidateSubcommand(args[1], SkillsSubcommands);
        return subcommandError != 0
            ? subcommandError
            : CliArgValidationSupport.ValidateOptions(args[2..], NoValueOptions, SkillsBooleanOptions);
    }

    public static int ValidateDiffArgs(string[] args) =>
        CliArgValidationSupport.ValidateOptionsUnlessHelp(args, DiffValueOptions, DiffBooleanOptions);

    public static int ValidateHotspotArgs(string[] args) =>
        CliArgValidationSupport.ValidateOptionsAndPositionals(
            args, HotspotValueOptions, HotspotBooleanOptions, "hotspot");

    public static int ValidateDupArgs(string[] args) =>
        CliArgValidationSupport.ValidateOptionsAndPositionals(args, DupValueOptions, DupBooleanOptions, "dup");

    public static int ValidateQueryArgs(string[] args) =>
        CliArgValidationSupport.ValidateOptionsAndPositionals(args, QueryValueOptions, QueryBooleanOptions, "query");

    public static int ValidateTrendArgs(string[] args) =>
        CliArgValidationSupport.ValidateOptionsUnlessHelp(args, TrendValueOptions, TrendBooleanOptions);

    public static int ValidateCalibrateArgs(string[] args) =>
        CliArgValidationSupport.ValidateOptionsUnlessHelp(args, CalibrateValueOptions, CalibrateBooleanOptions);

    public static int ValidateStatuslineArgs(string[] args) =>
        CliArgValidationSupport.ValidateOptionsAndPositionals(
            args, StatuslineValueOptions, StatuslineBooleanOptions, "statusline");

    public static int ValidateBadgeArgs(string[] args) =>
        CliArgValidationSupport.ValidateOptionsAndPositionals(args, BadgeValueOptions, BadgeBooleanOptions, "badge");

    public static int ValidateMetricsArgs(string[] args) =>
        CliArgValidationSupport.ValidateOptionsAndPositionals(
            args, MetricsValueOptions, MetricsBooleanOptions, "metrics");

    public static int ValidateSchemaArgs(string[] args) =>
        CliArgValidationSupport.ValidateOptionsAndPositionals(
            args, NoValueOptions, SchemaBooleanOptions, "schema");

    public static int ValidateMcpArgs(string[] args) =>
        CliArgValidationSupport.ValidateOptionsAndPositionals(args, NoValueOptions, McpBooleanOptions, "mcp");

    public static int ValidateBaselineArgs(string[] args)
    {
        if (CliArgValidationSupport.IsHelpRequest(args) || args.Length == 0)
            return 0;

        var subcommandError = CliArgValidationSupport.ValidateSubcommand(args[0], BaselineSubcommands);
        return subcommandError != 0
            ? subcommandError
            : CliArgValidationSupport.ValidateOptions(
                args[1..], BaselineCreateValueOptions, BaselineCreateBooleanOptions);
    }

    public static int ValidateTopLevelCommand(string command) =>
        TopLevelCommands.Contains(command)
            ? 0
            : CliArgValidationSupport.ReportUnknown(
                "subcommand",
                command,
                TopLevelCommands.Where(candidate => candidate is not "help" and not "version"));
}
