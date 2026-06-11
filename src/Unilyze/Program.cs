using System.Text.Json;
using Unilyze;

if (args.Length >= 1 && !args[0].StartsWith('-'))
{
    var topLevelError = ProgramHelpers.ValidateTopLevelCommand(args[0]);
    if (topLevelError != 0)
        return topLevelError;
}

if (args.Length >= 1 && args[0] == "diff")
    return DiffRunner.Run(args[1..]);
if (args.Length >= 1 && args[0] == "hotspot")
    return HotspotRunner.Run(args[1..]);
if (args.Length >= 1 && args[0] == "trend")
    return TrendRunner.Run(args[1..]);
if (args.Length >= 1 && args[0] == "metrics")
    return MetricsRunner.Run(args[1..]);
if (args.Length >= 1 && args[0] == "schema")
    return SchemaRunner.Run(args[1..]);
if (args.Length >= 1 && args[0] == "statusline")
    return StatuslineRunner.Run(args[1..]);
if (args.Length >= 1 && args[0] == "badge")
    return BadgeRunner.Run(args[1..]);
if (args.Length >= 1 && args[0] == "config")
    return ConfigRunner.Run(args[1..]);
if (args.Length >= 1 && args[0] == "query")
    return QueryRunner.Run(args[1..]);
if (args.Length >= 1 && args[0] == "baseline")
    return BaselineRunner.Run(args[1..]);
if (args.Length >= 1 && args[0] == "calibrate")
    return CalibrateRunner.Run(args[1..]);

var opts = ProgramHelpers.ParseOptions(args);

if (args.Length >= 1 && args[0] is "skills")
    return SkillInstaller.Run(args);

if (opts.ContainsKey("-h") || opts.ContainsKey("--help") || (args.Length == 1 && args[0] is "help"))
    return PrintUsage();
if (opts.ContainsKey("-v") || opts.ContainsKey("--version") || (args.Length == 1 && args[0] is "version"))
    return PrintVersion();

var analyzeUsageError = ProgramHelpers.ValidateAnalyzeOptions(args);
if (analyzeUsageError != 0)
    return analyzeUsageError;
if (ProgramHelpers.HasFlagWithoutValue(args, "--baseline"))
{
    Console.Error.WriteLine("--baseline requires a file path.");
    return 1;
}
var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
var input = opts.GetValueOrDefault("-i") ?? opts.GetValueOrDefault("--input");
var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
var prefix = opts.GetValueOrDefault("--prefix");
var assembly = opts.GetValueOrDefault("-a") ?? opts.GetValueOrDefault("--assembly");
var formatStr = opts.GetValueOrDefault("-f") ?? opts.GetValueOrDefault("--format");
var noOpen = opts.ContainsKey("--no-open");
var cliExcludeDirs = ProgramHelpers.ParseMultiValueOption(args, "--exclude-dir");
var cliProfile = opts.GetValueOrDefault("--profile");
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

OutputFormat format;
try { format = ProgramHelpers.ResolveFormat(formatStr, output); }
catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 1; }

try
{
    string json;
    AnalysisResult result;
    var resolved = new ResolvedAnalysisConfig(
        EffectiveSmellThresholds.Default,
        SmellThresholdProfiles.DefaultProfileName,
        new HashSet<CodeSmellKind>(),
        DisableCycles: false,
        InformationalSmellKinds: new HashSet<CodeSmellKind>(),
        SmellOverrides: null);

    if (input != null)
    {
        json = File.ReadAllText(input);
        result = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.AnalysisResult)
                 ?? throw new InvalidOperationException("Failed to parse JSON input");
    }
    else
    {
        var projectRoot = ProgramHelpers.ResolveProjectRoot(path!);
        var config = UnilyzeConfig.LoadMerged(projectRoot, cliExcludeDirs, cliProfile);
        resolved = config.ResolveAnalysisConfig();
        result = AnalysisPipeline.Build(
            path!, prefix, assembly, config.ExcludeDirs, requestedLevel,
            excludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
            applyAnyDepthExcludes: !config.DisableDefaultExcludes,
            analysisConfig: resolved);

        var baselinePath = ProgramHelpers.ResolveBaselineOption(opts, config);
        var baselineError = ProgramHelpers.TryApplyBaseline(result, projectRoot, baselinePath, out result);
        if (baselineError is 1)
            return 1;

        json = JsonSerializer.Serialize(result, AnalysisJsonContext.Default.AnalysisResult);
    }

    if (format == OutputFormat.Html)
    {
        var htmlPath = output ?? Path.Combine(Path.GetTempPath(), $"unilyze-{Path.GetFileName(result.ProjectPath)}.html");

        var html = HtmlFormatter.Generate(json, result.ProjectPath);
        File.WriteAllText(htmlPath, html);
        Console.Error.WriteLine($"Written to {htmlPath}");

        var jsonPath = Path.ChangeExtension(htmlPath, ".json");
        File.WriteAllText(jsonPath, json);
        Console.Error.WriteLine($"Written to {jsonPath}");

        if (output == null && !noOpen)
            ProgramHelpers.TryOpenInBrowser(htmlPath);

        return 0;
    }

    if (format == OutputFormat.Json)
        return ProgramHelpers.WriteOutput(json, output);

    if (format == OutputFormat.Sarif)
    {
        var sarif = SarifFormatter.Generate(result, resolved.Thresholds);
        return ProgramHelpers.WriteOutput(sarif, output);
    }

    if (format == OutputFormat.Markdown)
    {
        Console.Error.WriteLine("Unsupported format: 'markdown'");
        return 1;
    }

    Console.Error.WriteLine($"Unsupported format: '{format.ToString().ToLower()}'");
    return 1;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"Invalid JSON input: {ex.Message}");
    return 1;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int PrintVersion()
{
    Console.WriteLine($"unilyze {typeof(TypeAnalyzer).Assembly.GetName().Version?.ToString(3) ?? "0.1.0"}");
    return 0;
}

static int PrintUsage()
{
    Console.WriteLine("""
unilyze - Static analyzer for Unity projects

Usage:
  unilyze                                  Analyze current directory and open in browser
  unilyze diff <before.json> <after.json>  Compare two analysis snapshots
  unilyze hotspot                          Identify refactoring hotspots (git churn x complexity)
  unilyze query --worst 5 -i snapshot.json Per-type evidence packs for agent grounding
  unilyze trend <dir-of-jsons>             Show quality trend across multiple snapshots
  unilyze -p <path>                        Analyze project and open in browser
  unilyze -p <path> --no-open              Analyze project and write HTML/JSON without opening a browser
  unilyze -p <path> -o graph.html          Save HTML viewer (+ JSON) to file
  unilyze -p <path> -f json                Output JSON to stdout
  unilyze -p <path> -f sarif -o report.sarif  Output SARIF for GitHub Code Scanning
  unilyze -i result.json -o graph.html     Generate HTML from existing JSON
  unilyze skills install --claude           Install skills for AI coding tools
  unilyze badge -p <path> -o badge.json    Output shields.io endpoint badge JSON

Options:
  -p, --path         Unity project root or Assets directory (default: .)
  -i, --input        Use existing JSON instead of analyzing
  -o, --output       Output file path (format inferred from extension: .html, .json, .sarif)
  -f, --format       Output format: html, json, sarif (default: html)
  -a, --assembly     Filter by assembly name (exact or suffix match, e.g. "Domain" matches "App.Domain")
      --prefix       Filter asmdef names by prefix (auto-detected from common dot-prefix if omitted)
      --exclude-dir  Exclude directory from analysis (repeatable, relative to project root)
      --level        Pin analysis level: syntax, core, full, complete
                     (caps auto-resolved level; fails if the level cannot be reached)
      --baseline     Suppress known smells from a baseline file (see 'unilyze baseline create')
      --profile      Built-in smell threshold profile (default: default; unity for Unity role-aware thresholds)
      --no-open      Do not open the generated HTML in a browser
  -v, --version      Show version
  -h, --help         Show this help

Configuration:
  Settings are loaded from (all scopes merged additively):
    Global:  $XDG_CONFIG_HOME/unilyze/config.json (default: ~/.config/unilyze/config.json)
    Project: <project-root>/.unilyze.json
  Example .unilyze.json:
    { "excludeDirs": ["Assets/Plugins", "Assets/ThirdParty"], "profile": "unity" }
  Inline suppression (see README "Suppressing findings"):
    // unilyze-disable-next-line UNI014 -- reason
    // unilyze-disable UNI002

Subcommands:
  baseline        Snapshot and suppress known code smells (run 'unilyze baseline --help')
  calibrate       Derive smell-threshold candidates from analysis snapshots
  badge           Output shields.io endpoint badge JSON
  config          Manage configuration (run 'unilyze config --help' for details)
  metrics         Show metric definitions and code smell thresholds
  query           Per-type evidence packs for agent grounding (run 'unilyze query --help')
  schema          Show JSON output field reference
  skills          Manage skills for AI coding tools (run 'unilyze skills' for details)
  statusline      Output compact code health for status line display

Exit codes (all commands):
  0  Success / gate passed
  1  Usage error (unknown subcommand/option, invalid argument, file not found, etc.)
  2  Quality gate failed (badge/diff with --fail-under, --fail-over, or --fail-on-regression)
""");
    return 0;
}
