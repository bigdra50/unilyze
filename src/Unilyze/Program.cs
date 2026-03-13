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

    Console.Error.WriteLine($"Unsupported format: '{format.ToString().ToLower()}'");
    return 1;
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

    IReadOnlyList<AsmdefInfo> targets;
    if (asmdefs.Count == 0)
    {
        // No .asmdef files: treat entire directory as a single assembly
        targets = [new AsmdefInfo("Assembly-CSharp", assetsDir, [])];
    }
    else
    {
        prefix ??= DetectCommonPrefix(asmdefs);
        targets = FilterAssemblies(asmdefs, prefix, assemblyFilter);
    }

    var allTypes = new List<TypeNodeInfo>();
    var allSyntaxTrees = new List<Microsoft.CodeAnalysis.SyntaxTree>();
    foreach (var asm in targets)
    {
        var result = TypeAnalyzer.AnalyzeDirectoryWithTrees(asm.Directory, asm.Name);
        allTypes.AddRange(result.Types);
        allSyntaxTrees.AddRange(result.SyntaxTrees);
    }

    var deps = TypeAnalyzer.BuildDependencies(allTypes);

    var typeMetrics = CodeHealthCalculator.ComputeTypeMetrics(allTypes);

    // Build Compilation for SemanticModel-based analysis (LCOM etc.)
    var projectRoot = ResolveProjectRoot(path);
    var compilationResult = CompilationFactory.Create(projectRoot, allSyntaxTrees);
    var analysisLevel = compilationResult.Level.ToString();

    if (compilationResult.Level != AnalysisLevel.SyntaxOnly)
        Console.Error.WriteLine($"Analysis level: {analysisLevel}");

    // Compute LCOM and code smells with SemanticModel when available
    typeMetrics = EnrichWithSemanticAnalysis(typeMetrics, allTypes, allSyntaxTrees, compilationResult);

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
        typeMetrics,
        analysisLevel);
}

static IReadOnlyList<TypeMetrics> EnrichWithSemanticAnalysis(
    IReadOnlyList<TypeMetrics> typeMetrics,
    IReadOnlyList<TypeNodeInfo> allTypes,
    IReadOnlyList<Microsoft.CodeAnalysis.SyntaxTree> syntaxTrees,
    CompilationResult compilationResult)
{
    // O(1) lookups
    var treeByPath = new Dictionary<string, Microsoft.CodeAnalysis.SyntaxTree>(StringComparer.Ordinal);
    var sourceSet = compilationResult.Compilation?.SyntaxTrees ?? syntaxTrees;
    foreach (var tree in sourceSet)
    {
        if (!string.IsNullOrEmpty(tree.FilePath))
            treeByPath.TryAdd(tree.FilePath, tree);
    }

    var typeInfoByName = new Dictionary<string, TypeNodeInfo>();
    foreach (var t in allTypes)
        typeInfoByName.TryAdd(t.Name, t);

    // Build typeName -> TypeDeclarationSyntax lookup
    var typeDeclLookup = new Dictionary<string, Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>();
    foreach (var type in allTypes)
    {
        if (type.Kind is "enum" or "delegate") continue;
        if (!treeByPath.TryGetValue(type.FilePath, out var tree)) continue;

        var root = tree.GetRoot();
        var baseName = type.Name.Split('<')[0];
        var typeDecl = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
            .FirstOrDefault(td => td.Identifier.Text == baseName);
        if (typeDecl is not null)
            typeDeclLookup.TryAdd(type.Name, typeDecl);
    }

    // SemanticModel cache (one per tree)
    var modelCache = new Dictionary<Microsoft.CodeAnalysis.SyntaxTree, Microsoft.CodeAnalysis.SemanticModel>();

    var enriched = new List<TypeMetrics>(typeMetrics.Count);
    foreach (var metrics in typeMetrics)
    {
        double? lcom = null;
        if (typeDeclLookup.TryGetValue(metrics.TypeName, out var typeDecl))
        {
            Microsoft.CodeAnalysis.SemanticModel? model = null;
            if (compilationResult.Compilation is not null)
            {
                var tree = typeDecl.SyntaxTree;
                if (!modelCache.TryGetValue(tree, out model))
                {
                    model = compilationResult.Compilation.GetSemanticModel(tree);
                    modelCache[tree] = model;
                }
            }

            lcom = LcomCalculator.Calculate(typeDecl, model);
        }

        typeInfoByName.TryGetValue(metrics.TypeName, out var typeInfo);
        var smells = CodeSmellDetector.Detect(metrics, typeInfo!, lcom);

        enriched.Add(metrics with
        {
            Lcom = lcom,
            CodeSmells = smells.Count > 0 ? smells : null
        });
    }

    return enriched;
}

static string ResolveProjectRoot(string path)
{
    // Walk up to find ProjectSettings/ProjectVersion.txt
    var dir = Path.GetFullPath(path);
    for (var i = 0; i < 5; i++)
    {
        if (File.Exists(Path.Combine(dir, "ProjectSettings", "ProjectVersion.txt")))
            return dir;
        var parent = Directory.GetParent(dir)?.FullName;
        if (parent is null || parent == dir) break;
        dir = parent;
    }

    return Path.GetFullPath(path);
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
  unilyze -i result.json -o graph.html     Generate HTML from existing JSON

Options:
  -p, --path      Unity project root or Assets directory (default: .)
  -i, --input     Use existing JSON instead of analyzing
  -o, --output    Output file path (format inferred from extension: .html, .json)
  -f, --format    Output format: html, json (default: html)
  -a, --assembly  Filter by assembly name (e.g. App.Domain)
      --prefix    Filter asmdef names by prefix (auto-detected if omitted)
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
            _ => throw new ArgumentException($"Unknown format: '{formatStr}'. Valid formats: json, html")
        };
    }

    if (output != null)
    {
        return Path.GetExtension(output).ToLowerInvariant() switch
        {
            ".html" or ".htm" => OutputFormat.Html,
            ".json" => OutputFormat.Json,
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
