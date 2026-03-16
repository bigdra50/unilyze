using System.Text.Json;
using Unilyze;

if (args.Length >= 1 && args[0] == "diff")
    return RunDiff(args[1..]);
if (args.Length >= 1 && args[0] == "hotspot")
    return RunHotspot(args[1..]);
if (args.Length >= 1 && args[0] == "trend")
    return RunTrend(args[1..]);
if (args.Length >= 1 && args[0] == "metrics")
    return PrintMetrics();
if (args.Length >= 1 && args[0] == "schema")
    return PrintSchema();

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
var noOpen = opts.ContainsKey("--no-open");

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

        if (output == null && !noOpen)
            TryOpenInBrowser(htmlPath);

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
  unilyze hotspot                          Identify refactoring hotspots (git churn x complexity)
  unilyze trend <dir-of-jsons>             Show quality trend across multiple snapshots
  unilyze -p <path>                        Analyze project and open in browser
  unilyze -p <path> --no-open              Analyze project and write HTML/JSON without opening a browser
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
  -a, --assembly  Filter by assembly name (exact or suffix match, e.g. "Domain" matches "App.Domain")
      --prefix    Filter asmdef names by prefix (auto-detected from common dot-prefix if omitted)
      --no-open   Do not open the generated HTML in a browser
  -v, --version   Show version
  -h, --help      Show this help

Subcommands:
  metrics         Show metric definitions and code smell thresholds
  schema          Show JSON output field reference
  skills          Manage skills for AI coding tools (run 'unilyze skills' for details)

Exit codes:
  0  Success
  1  Error (invalid option, file not found, etc.)
""");
    return 0;
}


static int PrintMetrics()
{
    Console.WriteLine("""
    unilyze metrics - Metric definitions and thresholds

    Metrics (per method):
      CycCC    Cyclomatic Complexity (McCabe 1976). Count of decision points + 1.
               Counted: if, case, for, foreach, while, do, catch, ?:, ?., ??,
               &&, ||, goto, switch expression arm, bool & / bool |.
      CogCC    Cognitive Complexity (SonarSource). Nesting-aware complexity.
               Structural nodes (if, switch, for, while, catch) add +1 + nesting level.
               Logical operators (&&, ||, and, or) add +1 on kind change only.
               goto and direct recursion add flat +1.
      MI       Maintainability Index (0-100). Based on Halstead Volume, CycCC, LOC.
               >80 good, 60-80 moderate, <60 poor.
      Halstead Halstead complexity measures from operator/operand counts.
               Volume (V) = N * log2(n). Difficulty (D) = (n1/2) * (N2/n2).
               Effort (E) = D * V. EstimatedBugs (B) = E^(2/3) / 3000.

    Metrics (per type):
      WMC      Weighted Methods per Class. Sum of CycCC for all methods.
      NOC      Number of Children. Direct subclass/implementer count.
      RFC      Response For a Class. Methods in class + unique external methods called.
      LCOM     Lack of Cohesion of Methods (Henderson-Sellers).
               Formula: (avg(mA) - M) / (1 - M)
                 mA(f) = number of methods accessing field f
                 M     = number of instance methods (incl. constructors)
               0.0 = fully cohesive, 1.0 = fully dispersed.
               null when M <= 1 or no instance fields.
               Auto-properties excluded from field set.
      DIT      Depth of Inheritance Tree. Base class count above this type.
      CBO      Coupling Between Objects. Count of distinct external types referenced.
      Ca       Afferent Coupling. Number of types that depend on this type.
      Ce       Efferent Coupling. Number of types this type depends on.
      Inst     Instability = Ce / (Ca + Ce). 0.0 = stable, 1.0 = unstable.
      TypeRank PageRank-based importance score. Higher = more depended upon.
               damping=0.85, normalized to sum=1.0.

    Metrics (per assembly):
      A        Abstractness = (abstract classes + interfaces) / total types.
      DfMS     Distance from Main Sequence = |A + I - 1|. 0.0 = ideal balance.
      H        Relational Cohesion = (R + 1) / N. Internal relationship density.

    CodeHealth (per type, 1.0 - 10.0, higher is better):
      Weighted score from 6 factors:
        avgCogCC (25%), maxCogCC (20%), lineCount (15%),
        methodCount (10%), maxNestingDepth (15%), excessiveParamMethods (15%).

    CodeSmell detection thresholds:
      GodClass             lines >= 500 OR methods >= 20     (Critical: lines >= 1000)
      LongMethod           lines >= 80 OR CogCC >= 25       (Critical: lines >= 150 OR CogCC >= 40)
      HighComplexity       CycCC >= 15 OR CogCC >= 15
      DeepNesting          depth >= 4                        (Critical: depth >= 6)
      ExcessiveParameters  params > 5
      LowCohesion          LCOM >= 0.8
      HighCoupling         CBO >= 15
      DeepInheritance      DIT >= 5
      LowMaintainability   MI < 60
      CyclicDependency     type participates in a dependency cycle

    Performance analysis (per type, SemanticModel required):
      BoxingAllocation     Value type boxed to reference type (object, interface, virtual call)
      ClosureCapture       Lambda/anonymous method captures outer scope variable (heap alloc)
      ParamsArrayAllocation  Implicit array allocation for params parameter

    Exception flow analysis (per type):
      CatchAllException    catch (Exception) without rethrow
      MissingInnerException  throw new X() in catch without inner exception
      ThrowingSystemException  throw new Exception() directly (use specific exception)

    DI container detection:
      VContainer           Register<T>, RegisterInstance, [Inject] attribute
      Zenject              Bind<T>().To<T>(), BindInterfacesTo<T>()
    """);
    return 0;
}

static int PrintSchema()
{
    Console.WriteLine("""
    unilyze schema - JSON output field reference

    analyze (unilyze -f json):
      .projectPath                         string   Analyzed project path
      .analyzedAt                          string   ISO 8601 timestamp
      .analysisLevel                       string?  "SyntaxOnly" or "Semantic"
      .assemblies[]                        object   Assembly info
        .name                              string   Assembly name (from .asmdef)
        .metrics.abstractness              float    Abstractness (0.0-1.0)
        .metrics.distanceFromMainSequence  float?   |A + I - 1| (0.0 = ideal)
        .metrics.relationalCohesion        float?   (R + 1) / N
        .sourceFiles[]                     string   Relative .cs file paths
        .referencedAssemblies[]            string   Referenced assembly names
      .types[]                             object   Type topology nodes
        .name, .namespace, .assembly       string   Type identity
        .kind                              string   "Class","Interface","Struct","Enum","Delegate"
        .isPublic, .isSealed, .isAbstract  bool     Modifiers
      .dependencies[]                      object   Type-level dependencies
        .source, .target                   string   Type names
        .kind                              string   "Inheritance","Implementation","Association",
                                                    "Usage","Aggregation"
      .typeMetrics[]                       object   Per-type metrics (see below)

    typeMetrics[]:
      .typeName                            string   Type name (without namespace)
      .qualifiedName                       string?  Namespace.TypeName
      .namespace                           string   Namespace
      .assembly                            string   Assembly name
      .filePath                            string?  Source file path
      .startLine                           int?     Type declaration start line
      .typeId                              string?  Unique identifier
      .lineCount                           int      Total lines
      .methodCount                         int      Method count
      .codeHealth                          float    1.0-10.0 (higher is better)
      .lcom                                float?   LCOM-HS (0.0-1.0, null if N/A)
      .dit                                 int?     Depth of Inheritance Tree
      .cbo                                 int?     Coupling Between Objects
      .afferentCoupling                    int?     Ca
      .efferentCoupling                    int?     Ce
      .instability                         float?   Ce/(Ca+Ce)
      .wmc                                 int?     Weighted Methods per Class (sum of CycCC)
      .noc                                 int?     Number of Children (direct subclasses)
      .rfc                                 int?     Response For a Class
      .typeRank                            float?   PageRank importance score
      .boxingCount                         int?     Boxing operations detected
      .closureCaptureCount                 int?     Closure captures detected
      .paramsAllocationCount               int?     Implicit params array allocations
      .averageCognitiveComplexity          float    Avg CogCC across methods
      .averageCyclomaticComplexity         float    Avg CycCC across methods
      .maxCognitiveComplexity              int      Max CogCC
      .maxCyclomaticComplexity             int      Max CycCC
      .maxNestingDepth                     int      Max nesting depth
      .averageMaintainabilityIndex         float?   Avg MI
      .minMaintainabilityIndex             float?   Min MI
      .excessiveParameterMethodCount       int      Methods with params > 5
      .codeSmells[]                        object   Detected code smells
        .kind                              string   Smell category (see 'unilyze metrics')
        .severity                          string   "Warning" or "Critical"
        .message                           string   Human-readable description
        .methodName                        string?  Affected method (null for type-level)
      .methods[]                           object   Per-method metrics

    methods[]:
      .methodName                          string   Method name
      .cyclomaticComplexity                int      CycCC (base 1)
      .cognitiveComplexity                 int      CogCC
      .maxNestingDepth                     int      Max nesting depth
      .parameterCount                      int      Parameter count
      .lineCount                           int      Lines of code
      .startLine                           int?     Start line in source
      .maintainabilityIndex                float?   MI (0-100)
      .halsteadDifficulty                  float?   (n1/2) * (N2/n2)
      .halsteadEffort                      float?   Difficulty * Volume
      .halsteadEstimatedBugs               float?   E^(2/3) / 3000

    diff (unilyze diff):
      .beforePath, .afterPath              string   Compared file paths
      .beforeAnalyzedAt, .afterAnalyzedAt  string   Timestamps
      .summary                             object
        .improvedCount                     int      Types with better metrics
        .degradedCount                     int      Types with worse metrics
        .unchangedCount                    int      Unchanged types
        .addedCount                        int      New types
        .removedCount                      int      Removed types
      .improved[], .degraded[], .unchanged[], .added[], .removed[]
                                           TypeDiff[]  Per-type changes

    hotspot (unilyze hotspot):
      .projectPath                         string   Project path
      .since                               string   Git log period (e.g. "12.month")
      .topN                                int      Requested top N
      .hotspots[]                          object
        .typeName                          string   Type name
        .namespace                         string?  Namespace
        .filePath                          string?  File path
        .changeCount                       int      Git commit count touching this type
        .codeHealth                        float    CodeHealth score
        .hotspotScore                      float    changeCount * (10.0 - codeHealth)

    trend (unilyze trend):
      .snapshots[]                         object   Per-snapshot data
        .analyzedAt                        string   Timestamp
        .typeCount                         int      Number of types
        .averageCodeHealth                 float    Avg CodeHealth
        .codeSmellCount                    int      Total code smells
        .highComplexityTypeCount           int      Types with CycCC>=15 or CogCC>=15
        .averageCognitiveComplexity        float    Project-wide avg CogCC
      .summary                             object
        .snapshotCount                     int      Number of snapshots
        .codeHealthDelta                   float    First-to-last health change
        .codeSmellDelta                    int      First-to-last smell count change
    """);
    return 0;
}

static void TryOpenInBrowser(string path)
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
    catch (Exception ex) when (ex is FileNotFoundException or JsonException or IOException or UnauthorizedAccessException)
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
    if (args.Any(a => a is "-h" or "--help"))
        return PrintHotspotUsage();

    var opts = ProgramHelpers.ParseOptions(args);
    var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path");
    var input = opts.GetValueOrDefault("-i") ?? opts.GetValueOrDefault("--input");
    var since = opts.GetValueOrDefault("--since") ?? "12.month";
    var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");

    if (!int.TryParse(opts.GetValueOrDefault("-n") ?? "20", out var topN))
        topN = 20;

    path ??= ".";

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
    catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException or UnauthorizedAccessException)
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
      unilyze hotspot                                    Analyze current directory
      unilyze hotspot -p <path>                         Analyze specified project
      unilyze hotspot -p <path> -i analysis.json         Use existing analysis JSON
      unilyze hotspot -p <path> --since 6.month -n 10   Custom period and top N
      unilyze hotspot -p <path> -o hotspots.json         Save to file

    Options:
      -p, --path      Project root (default: ., used for git log)
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
