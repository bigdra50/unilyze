using System.Text.Json;
using Unilyze;

var opts = ParseOptions(args);

if (opts.ContainsKey("-h") || opts.ContainsKey("--help") || (args.Length == 1 && args[0] is "help"))
    return PrintUsage();
if (opts.ContainsKey("-v") || opts.ContainsKey("--version") || (args.Length == 1 && args[0] is "version"))
    return PrintVersion();
var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
var input = opts.GetValueOrDefault("-i") ?? opts.GetValueOrDefault("--input");
var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
var prefix = opts.GetValueOrDefault("--prefix");
var assembly = opts.GetValueOrDefault("-a") ?? opts.GetValueOrDefault("--assembly");
var scope = opts.GetValueOrDefault("-s") ?? opts.GetValueOrDefault("--scope") ?? "types";
var formatStr = opts.GetValueOrDefault("-f") ?? opts.GetValueOrDefault("--format");

// Determine output format: explicit -f > output extension > default (html)
OutputFormat format;
try { format = ResolveFormat(formatStr, output); }
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
        result = BuildAnalysisResult(path!, prefix, assembly);
        json = JsonSerializer.Serialize(result, AnalysisJsonContext.Default.AnalysisResult);
    }

    // Generate output
    var asmdefs = result.Assemblies.Select(a => new AsmdefInfo(a.Name, a.Directory, a.References)).ToList();

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

    var content = (scope, format) switch
    {
        ("assembly", OutputFormat.Csv) => Formatters.AssemblyToCsv(asmdefs, prefix),
        ("assembly", OutputFormat.Mermaid) => Formatters.AssemblyToMermaid(asmdefs, prefix),
        ("assembly", OutputFormat.Dot) => Formatters.AssemblyToDot(asmdefs, prefix),
        ("types", OutputFormat.Csv) => Formatters.TypesToCsv(result.Types, result.Dependencies, result.TypeMetrics),
        ("types", OutputFormat.Mermaid) => Formatters.TypesToMermaid(result.Types, result.Dependencies),
        ("types", OutputFormat.Dot) => Formatters.TypesToDot(result.Types, result.Dependencies),
        _ => (string?)null
    };

    if (content == null)
    {
        Console.Error.WriteLine($"Unsupported combination: scope='{scope}', format='{format.ToString().ToLower()}'");
        return 1;
    }

    return WriteOutput(content, output);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

// --- Core analysis ---

static AnalysisResult BuildAnalysisResult(string path, string? prefix, string? assemblyFilter)
{
    var assetsDir = ResolveAssetsDir(path);
    var asmdefs = AsmdefInfo.Discover(assetsDir);

    if (asmdefs.Count == 0)
        throw new InvalidOperationException($"No .asmdef files found under {assetsDir}");

    prefix ??= DetectCommonPrefix(asmdefs);

    var targets = FilterAssemblies(asmdefs, prefix, assemblyFilter);

    var allTypes = new List<TypeNodeInfo>();
    foreach (var asm in targets)
        allTypes.AddRange(TypeAnalyzer.AnalyzeDirectory(asm.Directory, asm.Name));

    var deps = TypeAnalyzer.BuildDependencies(allTypes);

    var typeMetrics = CodeHealthCalculator.ComputeTypeMetrics(allTypes);

    var assemblyInfos = targets.Select(a =>
    {
        var types = allTypes.Where(t => t.Assembly == a.Name).ToList();
        var metrics = AssemblyMetrics.Compute(a.Name, types);
        var asmTypeMetrics = typeMetrics.Where(m => m.Assembly == a.Name).ToList();
        var health = CodeHealthCalculator.ComputeAssemblyHealth(asmTypeMetrics);
        return new AssemblyInfo(a.Name, a.Directory, a.References, metrics, health);
    }).ToList();

    return new AnalysisResult(
        Path.GetFullPath(path),
        DateTimeOffset.UtcNow,
        assemblyInfos,
        allTypes,
        deps,
        typeMetrics);
}

static IReadOnlyList<AsmdefInfo> FilterAssemblies(
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
  unilyze -p <path>                        Analyze project and open in browser
  unilyze -p <path> -o graph.html          Save HTML viewer (+ JSON) to file
  unilyze -p <path> -f json                Output JSON to stdout
  unilyze -p <path> -f csv                 Output CSV to stdout
  unilyze -i result.json -o graph.html     Generate HTML from existing JSON

Options:
  -p, --path      Unity project root or Assets directory (default: .)
  -i, --input     Use existing JSON instead of analyzing
  -o, --output    Output file path (format inferred from extension: .html, .json)
  -f, --format    Output format: html, json, csv, mermaid, dot (default: html)
  -a, --assembly  Filter by assembly name (e.g. App.Domain)
      --prefix    Filter asmdef names by prefix (auto-detected if omitted)
  -s, --scope     Scope: types, assembly (default: types)
  -v, --version   Show version
  -h, --help      Show this help
""");
    return 0;
}

static OutputFormat ResolveFormat(string? formatStr, string? output)
{
    if (formatStr != null)
    {
        return formatStr.ToLowerInvariant() switch
        {
            "json" => OutputFormat.Json,
            "html" => OutputFormat.Html,
            "csv" => OutputFormat.Csv,
            "mermaid" => OutputFormat.Mermaid,
            "dot" => OutputFormat.Dot,
            _ => throw new ArgumentException($"Unknown format: '{formatStr}'. Valid formats: json, html, csv, mermaid, dot")
        };
    }

    if (output != null)
    {
        return Path.GetExtension(output).ToLowerInvariant() switch
        {
            ".html" or ".htm" => OutputFormat.Html,
            ".json" => OutputFormat.Json,
            ".csv" => OutputFormat.Csv,
            ".md" => OutputFormat.Mermaid,
            ".dot" or ".gv" => OutputFormat.Dot,
            _ => OutputFormat.Json
        };
    }

    return OutputFormat.Html;
}

static void OpenInBrowser(string path)
{
    var url = "file://" + Path.GetFullPath(path);
    if (OperatingSystem.IsMacOS())
        System.Diagnostics.Process.Start("open", url);
    else if (OperatingSystem.IsWindows())
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    else if (OperatingSystem.IsLinux())
        System.Diagnostics.Process.Start("xdg-open", url);
}

static string ResolveAssetsDir(string path)
{
    if (Directory.Exists(Path.Combine(path, "Assets")))
        return Path.Combine(path, "Assets");
    if (Path.GetFileName(path) == "Assets")
        return path;
    return path;
}

static string? DetectCommonPrefix(IReadOnlyList<AsmdefInfo> asmdefs)
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

static Dictionary<string, string> ParseOptions(string[] args)
{
    var opts = new Dictionary<string, string>();
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith('-'))
        {
            if (args[i] is "-h" or "--help" or "-v" or "--version")
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
