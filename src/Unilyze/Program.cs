using System.Text.Json;
using Unilyze;

if (args.Length >= 1 && args[0] == "diff")
    return RunDiff(args[1..]);
if (args.Length >= 1 && args[0] == "hotspot")
    return RunHotspot(args[1..]);
if (args.Length >= 1 && args[0] == "trend")
    return RunTrend(args[1..]);

var opts = ProgramHelpers.ParseOptions(args);

if (args.Length >= 1 && args[0] is "skills")
    return SkillInstaller.Run(args);

if (opts.ContainsKey("-h") || opts.ContainsKey("--help") || (args.Length == 1 && args[0] is "help"))
    return PrintUsage();
if (opts.ContainsKey("-v") || opts.ContainsKey("--version") || (args.Length == 1 && args[0] is "version"))
    return PrintVersion();
var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
var input = opts.GetValueOrDefault("-i") ?? opts.GetValueOrDefault("--input");
var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
var prefix = opts.GetValueOrDefault("--prefix");
var assembly = opts.GetValueOrDefault("-a") ?? opts.GetValueOrDefault("--assembly");
var formatStr = opts.GetValueOrDefault("-f") ?? opts.GetValueOrDefault("--format");

// Determine output format: explicit -f > output extension > default (html)
OutputFormat format;
try { format = ProgramHelpers.ResolveFormat(formatStr, output); }
catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 1; }

try
{
    // Source: existing JSON or fresh analysis
    string json;
    AnalysisResult result;

    if (input != null)
    {
        json = File.ReadAllText(input);
        result = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.AnalysisResult)
                 ?? throw new InvalidOperationException("Failed to parse JSON input");
    }
    else
    {
        result = AnalysisPipeline.Build(path!, prefix, assembly);
        json = JsonSerializer.Serialize(result, AnalysisJsonContext.Default.AnalysisResult);
    }

    // Generate output
    if (format == OutputFormat.Html)
    {
        var htmlPath = output ?? Path.Combine(Path.GetTempPath(), $"unilyze-{Path.GetFileName(result.ProjectPath)}.html");

        var html = HtmlFormatter.Generate(json, result.ProjectPath);
        File.WriteAllText(htmlPath, html);
        Console.Error.WriteLine($"Written to {htmlPath}");

        var jsonPath = Path.ChangeExtension(htmlPath, ".json");
        File.WriteAllText(jsonPath, json);
        Console.Error.WriteLine($"Written to {jsonPath}");

        if (output == null)
            OpenInBrowser(htmlPath);

        return 0;
    }

    if (format == OutputFormat.Json)
        return WriteOutput(json, output);

    if (format == OutputFormat.Sarif)
    {
        var sarif = SarifFormatter.Generate(result);
        return WriteOutput(sarif, output);
    }

    Console.Error.WriteLine($"Unsupported format: '{format.ToString().ToLower()}'");
    return 1;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

// --- Helpers ---

static int WriteOutput(string content, string? outputPath)
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
  unilyze hotspot -p <path>                Identify refactoring hotspots (git churn x complexity)
  unilyze trend <dir-of-jsons>             Show quality trend across multiple snapshots
  unilyze -p <path>                        Analyze project and open in browser
  unilyze -p <path> -o graph.html          Save HTML viewer (+ JSON) to file
  unilyze -p <path> -f json                Output JSON to stdout
  unilyze -p <path> -f sarif -o report.sarif  Output SARIF for GitHub Code Scanning
  unilyze -i result.json -o graph.html     Generate HTML from existing JSON
  unilyze skills install --claude           Install skills for AI coding tools

Options:
  -p, --path      Unity project root or Assets directory (default: .)
  -i, --input     Use existing JSON instead of analyzing
  -o, --output    Output file path (format inferred from extension: .html, .json, .sarif)
  -f, --format    Output format: html, json, sarif (default: html)
  -a, --assembly  Filter by assembly name (e.g. App.Domain)
      --prefix    Filter asmdef names by prefix (auto-detected if omitted)
  -v, --version   Show version
  -h, --help      Show this help

Subcommands:
  skills          Manage skills for AI coding tools (run 'unilyze skills' for details)
""");
    return 0;
}


static void OpenInBrowser(string path)
{
    var url = "file://" + Path.GetFullPath(path);
    if (OperatingSystem.IsMacOS())
        System.Diagnostics.Process.Start("open", url)?.Dispose();
    else if (OperatingSystem.IsWindows())
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
    else if (OperatingSystem.IsLinux())
        System.Diagnostics.Process.Start("xdg-open", url)?.Dispose();
}


static int RunDiff(string[] args)
{
    if (args.Length == 0 || args.Any(a => a is "-h" or "--help"))
        return PrintDiffUsage();

    var positional = args.Where(a => !a.StartsWith('-')).ToList();
    if (positional.Count < 2)
    {
        Console.Error.WriteLine("Usage: unilyze diff <before.json> <after.json> [-o output.json]");
        return 1;
    }

    var opts = ProgramHelpers.ParseOptions(args);
    var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");

    var beforePath = positional[0];
    var afterPath = positional[1];

    try
    {
        var beforeJson = File.ReadAllText(beforePath);
        var afterJson = File.ReadAllText(afterPath);

        var before = JsonSerializer.Deserialize(beforeJson, AnalysisJsonContext.Default.AnalysisResult)
                     ?? throw new InvalidOperationException($"Failed to parse: {beforePath}");
        var after = JsonSerializer.Deserialize(afterJson, AnalysisJsonContext.Default.AnalysisResult)
                    ?? throw new InvalidOperationException($"Failed to parse: {afterPath}");

        var diff = DiffCalculator.Compare(before, after);
        var json = JsonSerializer.Serialize(diff, AnalysisJsonContext.Default.DiffResult);

        PrintDiffSummary(diff);

        return WriteOutput(json, output);
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static void PrintDiffSummary(DiffResult diff)
{
    Console.Error.WriteLine($"Diff: {diff.BeforePath} -> {diff.AfterPath}");
    Console.Error.WriteLine($"  Improved:  {diff.Summary.ImprovedCount}");
    Console.Error.WriteLine($"  Degraded:  {diff.Summary.DegradedCount}");
    Console.Error.WriteLine($"  Unchanged: {diff.Summary.UnchangedCount}");
    Console.Error.WriteLine($"  Added:     {diff.Summary.AddedCount}");
    Console.Error.WriteLine($"  Removed:   {diff.Summary.RemovedCount}");

    if (diff.Degraded.Count > 0)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("Degraded types:");
        foreach (var t in diff.Degraded)
            Console.Error.WriteLine($"  {t.TypeKey}");
    }

    if (diff.Improved.Count > 0)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("Improved types:");
        foreach (var t in diff.Improved)
            Console.Error.WriteLine($"  {t.TypeKey}");
    }
}

static int PrintDiffUsage()
{
    Console.WriteLine("""
    unilyze diff - Compare two analysis snapshots

    Usage:
      unilyze diff <before.json> <after.json>              Output diff JSON to stdout
      unilyze diff <before.json> <after.json> -o out.json   Save diff to file

    Options:
      -o, --output    Output file path
      -h, --help      Show this help
    """);
    return 0;
}

static int RunHotspot(string[] args)
{
    if (args.Length == 0 || args.Any(a => a is "-h" or "--help"))
        return PrintHotspotUsage();

    var opts = ProgramHelpers.ParseOptions(args);
    var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path");
    var input = opts.GetValueOrDefault("-i") ?? opts.GetValueOrDefault("--input");
    var since = opts.GetValueOrDefault("--since") ?? "12.month";
    var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");

    if (!int.TryParse(opts.GetValueOrDefault("-n") ?? "20", out var topN))
        topN = 20;

    if (path == null)
    {
        Console.Error.WriteLine("Error: -p/--path is required for hotspot analysis");
        return 1;
    }

    try
    {
        IReadOnlyList<TypeMetrics> typeMetrics;
        if (input != null)
        {
            var json = File.ReadAllText(input);
            var result = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.AnalysisResult)
                         ?? throw new InvalidOperationException($"Failed to parse: {input}");
            typeMetrics = result.TypeMetrics ?? [];
        }
        else
        {
            var result = AnalysisPipeline.Build(path, null, null);
            typeMetrics = result.TypeMetrics ?? [];
        }

        var gitLogOutput = HotspotAnalyzer.RunGitLog(path, since);
        var changeFrequencies = HotspotAnalyzer.ParseGitLog(gitLogOutput);
        var hotspot = HotspotAnalyzer.Analyze(typeMetrics, changeFrequencies, path, since, topN);

        var hotspotJson = JsonSerializer.Serialize(hotspot, AnalysisJsonContext.Default.HotspotResult);

        PrintHotspotSummary(hotspot);

        return WriteOutput(hotspotJson, output);
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static void PrintHotspotSummary(HotspotResult hotspot)
{
    Console.Error.WriteLine($"Hotspot analysis: {hotspot.ProjectPath} (since {hotspot.Since})");
    Console.Error.WriteLine($"  Total hotspots: {hotspot.Hotspots.Count}");
    Console.Error.WriteLine();

    if (hotspot.Hotspots.Count == 0)
    {
        Console.Error.WriteLine("  No hotspots found.");
        return;
    }

    Console.Error.WriteLine("  Rank  Score   Churn  Health  Type");
    Console.Error.WriteLine("  ----  ------  -----  ------  ----");
    for (var i = 0; i < hotspot.Hotspots.Count; i++)
    {
        var h = hotspot.Hotspots[i];
        var typeName = string.IsNullOrEmpty(h.Namespace) ? h.TypeName : $"{h.Namespace}.{h.TypeName}";
        Console.Error.WriteLine($"  {i + 1,4}  {h.HotspotScore,6:F1}  {h.ChangeCount,5}  {h.CodeHealth,6:F1}  {typeName}");
    }
}

static int PrintHotspotUsage()
{
    Console.WriteLine("""
    unilyze hotspot - Identify refactoring hotspots (git churn x complexity)

    Usage:
      unilyze hotspot -p <path>                         Analyze and output hotspot JSON
      unilyze hotspot -p <path> -i analysis.json         Use existing analysis JSON
      unilyze hotspot -p <path> --since 6.month -n 10   Custom period and top N
      unilyze hotspot -p <path> -o hotspots.json         Save to file

    Options:
      -p, --path      Project root (required, used for git log)
      -i, --input     Existing analysis JSON (skip fresh analysis)
      --since          Git log period (default: 12.month)
      -n               Top N results (default: 20)
      -o, --output    Output file path
      -h, --help      Show this help
    """);
    return 0;
}

static int RunTrend(string[] args)
{
    if (args.Length == 0 || args.Any(a => a is "-h" or "--help"))
        return PrintTrendUsage();

    var positional = args.Where(a => !a.StartsWith('-')).ToList();
    if (positional.Count < 1)
    {
        Console.Error.WriteLine("Usage: unilyze trend <dir-of-jsons> [-o output.json]");
        return 1;
    }

    var opts = ProgramHelpers.ParseOptions(args);
    var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
    var dir = positional[0];

    if (!Directory.Exists(dir))
    {
        Console.Error.WriteLine($"Directory not found: {dir}");
        return 1;
    }

    try
    {
        var jsonFiles = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f)
            .ToList();

        if (jsonFiles.Count == 0)
        {
            Console.Error.WriteLine($"No JSON files found in: {dir}");
            return 1;
        }

        var results = new List<AnalysisResult>();
        foreach (var file in jsonFiles)
        {
            var json = File.ReadAllText(file);
            var result = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.AnalysisResult);
            if (result is null)
            {
                Console.Error.WriteLine($"Skipping invalid file: {file}");
                continue;
            }
            results.Add(result);
        }

        if (results.Count == 0)
        {
            Console.Error.WriteLine("No valid analysis results found.");
            return 1;
        }

        var trend = TrendAnalyzer.Analyze(results);
        var trendJson = JsonSerializer.Serialize(trend, AnalysisJsonContext.Default.TrendResult);

        PrintTrendSummary(trend);

        return WriteOutput(trendJson, output);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static void PrintTrendSummary(TrendResult trend)
{
    Console.Error.WriteLine($"Trend: {trend.Summary.SnapshotCount} snapshot(s)");
    Console.Error.WriteLine($"  CodeHealth delta:  {trend.Summary.CodeHealthDelta:+0.0;-0.0;0.0}");
    Console.Error.WriteLine($"  CodeSmell delta:   {trend.Summary.CodeSmellDelta:+0;-0;0}");

    if (trend.Snapshots.Count > 0)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("  Date                Types  Health  Smells  HighCC  AvgCogCC");
        Console.Error.WriteLine("  ------------------  -----  ------  ------  ------  --------");
        foreach (var s in trend.Snapshots)
        {
            Console.Error.WriteLine(
                $"  {s.AnalyzedAt:yyyy-MM-dd HH:mm}  {s.TypeCount,5}  {s.AverageCodeHealth,6:F1}  {s.CodeSmellCount,6}  {s.HighComplexityTypeCount,6}  {s.AverageCognitiveComplexity,8:F1}");
        }
    }
}

static int PrintTrendUsage()
{
    Console.WriteLine("""
    unilyze trend - Show quality trend across multiple snapshots

    Usage:
      unilyze trend <dir-of-jsons>              Output trend JSON to stdout
      unilyze trend <dir-of-jsons> -o out.json   Save trend to file

    Options:
      -o, --output    Output file path
      -h, --help      Show this help
    """);
    return 0;
}

