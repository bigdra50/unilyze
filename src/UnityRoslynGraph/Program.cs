using System.Text.Json;
using UnityRoslynGraph;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0];
var opts = ParseOptions(args[1..]);

return command switch
{
    "analyze" => RunAnalyze(opts),
    "format" => RunFormat(opts),
    "assembly" => RunAssembly(opts),
    "types" => RunTypes(opts),
    "diagram" => RunDiagram(opts),
    "-h" or "--help" or "help" => PrintUsage(),
    _ => Error($"Unknown command: {command}")
};

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

// --- Commands ---

static int RunAnalyze(Dictionary<string, string> opts)
{
    var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
    var prefix = opts.GetValueOrDefault("--prefix");
    var assembly = opts.GetValueOrDefault("-a") ?? opts.GetValueOrDefault("--assembly");
    var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");

    try
    {
        var result = BuildAnalysisResult(path, prefix, assembly);
        var json = JsonSerializer.Serialize(result, AnalysisJsonContext.Default.AnalysisResult);
        return WriteOutput(json, output);
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static int RunFormat(Dictionary<string, string> opts)
{
    var input = opts.GetValueOrDefault("-i") ?? opts.GetValueOrDefault("--input");
    var format = ParseFormat(opts.GetValueOrDefault("-f") ?? opts.GetValueOrDefault("--format") ?? "csv");
    var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
    var scope = opts.GetValueOrDefault("-s") ?? opts.GetValueOrDefault("--scope") ?? "types";

    if (input == null)
    {
        Console.Error.WriteLine("format command requires -i <json-file>");
        return 1;
    }

    if (format is OutputFormat.Drawio or OutputFormat.Html && output == null)
    {
        Console.Error.WriteLine($"{format.ToString().ToLower()} format requires -o <file>");
        return 1;
    }

    var json = File.ReadAllText(input);
    var result = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.AnalysisResult);
    if (result == null)
    {
        Console.Error.WriteLine("Failed to parse JSON input");
        return 1;
    }

    var asmdefs = result.Assemblies.Select(a => new AsmdefInfo(a.Name, a.Directory, a.References)).ToList();

    var content = (scope, format) switch
    {
        ("assembly", OutputFormat.Csv) => Formatters.AssemblyToCsv(asmdefs, null),
        ("assembly", OutputFormat.Mermaid) => Formatters.AssemblyToMermaid(asmdefs, null),
        ("assembly", OutputFormat.Dot) => Formatters.AssemblyToDot(asmdefs, null),
        ("types", OutputFormat.Csv) => Formatters.TypesToCsv(result.Types, result.Dependencies, result.TypeMetrics),
        ("types", OutputFormat.Mermaid) => Formatters.TypesToMermaid(result.Types, result.Dependencies),
        ("types", OutputFormat.Dot) => Formatters.TypesToDot(result.Types, result.Dependencies),
        ("types", OutputFormat.Drawio) => DrawioFormatter.TypesToDrawio(result.Types, result.Dependencies, result.TypeMetrics),
        (_, OutputFormat.Drawio) when scope == "diagram" => FormatDiagram(result, asmdefs, output!),
        (_, OutputFormat.Html) => HtmlFormatter.Generate(json, result.ProjectPath),
        _ => ""
    };

    return WriteOutput(content, output);
}

static string FormatDiagram(AnalysisResult result, IReadOnlyList<AsmdefInfo> asmdefs, string output)
{
    var typesByAssembly = result.Types
        .GroupBy(t => t.Assembly)
        .ToDictionary(g => g.Key, g => (IReadOnlyList<TypeNodeInfo>)g.ToList());
    var metrics = result.Assemblies.Select(a => a.Metrics).ToList();
    var healthMetrics = result.Assemblies
        .Where(a => a.HealthMetrics != null)
        .ToDictionary(a => a.Name, a => a.HealthMetrics!);
    return DrawioFormatter.GenerateMultiPage(
        asmdefs, metrics, typesByAssembly, result.Dependencies, null,
        healthMetrics, result.TypeMetrics);
}

// --- Legacy shortcut commands ---

static int RunAssembly(Dictionary<string, string> opts)
{
    var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
    var format = ParseFormat(opts.GetValueOrDefault("-f") ?? opts.GetValueOrDefault("--format") ?? "csv");
    var prefix = opts.GetValueOrDefault("--prefix");
    var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");

    if (format == OutputFormat.Drawio && output == null)
    {
        Console.Error.WriteLine("drawio format requires -o <file>");
        return 1;
    }

    var assetsDir = ResolveAssetsDir(path);
    var asmdefs = AsmdefInfo.Discover(assetsDir);

    if (asmdefs.Count == 0)
    {
        Console.Error.WriteLine($"No .asmdef files found under {assetsDir}");
        return 1;
    }

    prefix ??= DetectCommonPrefix(asmdefs);

    if (format == OutputFormat.Drawio)
    {
        var filtered = prefix != null
            ? asmdefs.Where(a => a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList()
            : asmdefs.ToList();
        var allTypes = new List<TypeNodeInfo>();
        var metrics = filtered.Select(a =>
        {
            var types = TypeAnalyzer.AnalyzeDirectory(a.Directory, a.Name);
            allTypes.AddRange(types);
            return AssemblyMetrics.Compute(a.Name, types);
        }).ToList();
        var typeMetrics = CodeHealthCalculator.ComputeTypeMetrics(allTypes);
        var healthDict = filtered
            .Select(a => (a.Name, Health: CodeHealthCalculator.ComputeAssemblyHealth(
                typeMetrics.Where(m => m.Assembly == a.Name).ToList())))
            .Where(x => x.Health != null)
            .ToDictionary(x => x.Name, x => x.Health!);

        return WriteOutput(DrawioFormatter.AssemblyToDrawio(asmdefs, metrics, prefix, healthDict), output);
    }

    var result = format switch
    {
        OutputFormat.Csv => Formatters.AssemblyToCsv(asmdefs, prefix),
        OutputFormat.Mermaid => Formatters.AssemblyToMermaid(asmdefs, prefix),
        OutputFormat.Dot => Formatters.AssemblyToDot(asmdefs, prefix),
        _ => ""
    };
    return WriteOutput(result, output);
}

static int RunTypes(Dictionary<string, string> opts)
{
    var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
    var assembly = opts.GetValueOrDefault("-a") ?? opts.GetValueOrDefault("--assembly");
    var format = ParseFormat(opts.GetValueOrDefault("-f") ?? opts.GetValueOrDefault("--format") ?? "csv");
    var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");

    if (format == OutputFormat.Drawio && output == null)
    {
        Console.Error.WriteLine("drawio format requires -o <file>");
        return 1;
    }

    var assetsDir = ResolveAssetsDir(path);
    var asmdefs = AsmdefInfo.Discover(assetsDir);

    if (asmdefs.Count == 0)
    {
        Console.Error.WriteLine($"No .asmdef files found under {assetsDir}");
        return 1;
    }

    IReadOnlyList<AsmdefInfo> targets = assembly != null
        ? asmdefs.Where(a => a.Name.Equals(assembly, StringComparison.OrdinalIgnoreCase)
                             || a.Name.EndsWith("." + assembly, StringComparison.OrdinalIgnoreCase))
            .ToList()
        : asmdefs;

    if (targets.Count == 0)
    {
        Console.Error.WriteLine($"Assembly '{assembly}' not found. Available:");
        foreach (var a in asmdefs)
            Console.Error.WriteLine($"  {a.Name}");
        return 1;
    }

    var allTypes = new List<TypeNodeInfo>();
    foreach (var asm in targets)
        allTypes.AddRange(TypeAnalyzer.AnalyzeDirectory(asm.Directory, asm.Name));

    var deps = TypeAnalyzer.BuildDependencies(allTypes);

    var result = format switch
    {
        OutputFormat.Csv => Formatters.TypesToCsv(allTypes, deps),
        OutputFormat.Mermaid => Formatters.TypesToMermaid(allTypes, deps),
        OutputFormat.Dot => Formatters.TypesToDot(allTypes, deps),
        OutputFormat.Drawio => DrawioFormatter.TypesToDrawio(allTypes, deps),
        _ => ""
    };
    return WriteOutput(result, output);
}

static int RunDiagram(Dictionary<string, string> opts)
{
    var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
    var prefix = opts.GetValueOrDefault("--prefix");
    var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");

    if (output == null)
    {
        Console.Error.WriteLine("diagram command requires -o <file>");
        return 1;
    }

    var assetsDir = ResolveAssetsDir(path);
    var asmdefs = AsmdefInfo.Discover(assetsDir);

    if (asmdefs.Count == 0)
    {
        Console.Error.WriteLine($"No .asmdef files found under {assetsDir}");
        return 1;
    }

    prefix ??= DetectCommonPrefix(asmdefs);

    var filtered = prefix != null
        ? asmdefs.Where(a => a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList()
        : asmdefs.ToList();

    var typesByAssembly = new Dictionary<string, IReadOnlyList<TypeNodeInfo>>();
    var allTypes = new List<TypeNodeInfo>();
    foreach (var asm in filtered)
    {
        var types = TypeAnalyzer.AnalyzeDirectory(asm.Directory, asm.Name);
        typesByAssembly[asm.Name] = types;
        allTypes.AddRange(types);
    }

    var allDeps = TypeAnalyzer.BuildDependencies(allTypes);
    var typeMetrics = CodeHealthCalculator.ComputeTypeMetrics(allTypes);
    var metrics = filtered.Select(a =>
        AssemblyMetrics.Compute(a.Name, typesByAssembly.GetValueOrDefault(a.Name) ?? [])).ToList();
    var healthDict = filtered
        .Select(a => (a.Name, Health: CodeHealthCalculator.ComputeAssemblyHealth(
            typeMetrics.Where(m => m.Assembly == a.Name).ToList())))
        .Where(x => x.Health != null)
        .ToDictionary(x => x.Name, x => x.Health!);

    var result = DrawioFormatter.GenerateMultiPage(
        asmdefs, metrics, typesByAssembly, allDeps, prefix, healthDict, typeMetrics);
    return WriteOutput(result, output);
}

// --- Shared helpers ---

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

static int PrintUsage()
{
    Console.WriteLine("""
        unity-roslyn-graph - Dependency graph generator for Unity projects

        Usage:
          unity-roslyn-graph analyze  -p <path> [--prefix <prefix>] [-a <assembly>] [-o <file>]
          unity-roslyn-graph format   -i <json> [-f csv|mermaid|dot|drawio] [-s assembly|types|diagram] [-o <file>]
          unity-roslyn-graph assembly [-p <path>] [-f csv|mermaid|dot|drawio] [--prefix <prefix>] [-o <file>]
          unity-roslyn-graph types    [-p <path>] [-f csv|mermaid|dot|drawio] [-a <assembly>] [-o <file>]
          unity-roslyn-graph diagram  -p <path> [--prefix <prefix>] -o <file>

        Commands:
          analyze     Analyze project and output JSON intermediate format
          format      Convert JSON to visualization format
          assembly    Generate assembly (asmdef) dependency graph (shortcut)
          types       Generate type dependency graph (shortcut)
          diagram     Generate multi-page draw.io diagram (shortcut)

        Options:
          -p, --path      Unity project root or Assets directory (default: .)
          -f, --format    Output format: csv, mermaid, dot, drawio (default: csv)
          -a, --assembly  Assembly name to analyze (e.g. App.Domain)
              --prefix    Filter asmdef names by prefix (auto-detected if omitted)
          -o, --output    Output file path (required for drawio format and diagram command)
          -i, --input     Input JSON file (for format command)
          -s, --scope     Format scope: assembly, types, diagram (default: types)
        """);
    return 0;
}

static int Error(string msg)
{
    Console.Error.WriteLine(msg);
    PrintUsage();
    return 1;
}

static string ResolveAssetsDir(string path)
{
    if (Directory.Exists(Path.Combine(path, "Assets")))
        return Path.Combine(path, "Assets");
    if (Path.GetFileName(path) == "Assets")
        return path;
    return path;
}

static OutputFormat ParseFormat(string s) => s.ToLowerInvariant() switch
{
    "csv" => OutputFormat.Csv,
    "mermaid" => OutputFormat.Mermaid,
    "dot" => OutputFormat.Dot,
    "drawio" => OutputFormat.Drawio,
    "html" => OutputFormat.Html,
    _ => OutputFormat.Csv
};

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
        if (args[i].StartsWith('-') && i + 1 < args.Length)
        {
            opts[args[i]] = args[i + 1];
            i++;
        }
    }
    return opts;
}
