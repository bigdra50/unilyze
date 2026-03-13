using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

internal static class AnalysisPipeline
{
    public static AnalysisResult Build(string path, string? prefix, string? assemblyFilter)
    {
        var assetsDir = ProgramHelpers.ResolveAssetsDir(path);
        var asmdefs = AsmdefInfo.Discover(assetsDir);

        IReadOnlyList<AsmdefInfo> targets;
        if (asmdefs.Count == 0)
        {
            targets = [new AsmdefInfo("Assembly-CSharp", assetsDir, [])];
        }
        else
        {
            prefix ??= ProgramHelpers.DetectCommonPrefix(asmdefs);
            targets = ProgramHelpers.FilterAssemblies(asmdefs, prefix, assemblyFilter);
        }

        var projectRoot = ProgramHelpers.ResolveProjectRoot(path);
        var csprojInfo = ResolveCsprojInfo(projectRoot);

        var resolved = UnityDllResolver.Resolve(projectRoot);
        var preprocessorSymbols = MergePreprocessorSymbols(projectRoot, csprojInfo);

        var (allTypes, allSyntaxTrees) = CollectTypes(targets, preprocessorSymbols);

        var deps = DependencyBuilder.Build(allTypes);
        var typeMetrics = CodeHealthCalculator.ComputeTypeMetrics(allTypes);

        var couplingMap = CouplingMetricsCalculator.Calculate(deps, allTypes);
        typeMetrics = EnrichWithCouplingMetrics(typeMetrics, couplingMap);

        var compilationResult = CompilationFactory.Create(resolved, allSyntaxTrees, csprojInfo);
        var analysisLevel = compilationResult.Level.ToString();

        if (compilationResult.Level != AnalysisLevel.SyntaxOnly)
            Console.Error.WriteLine($"Analysis level: {analysisLevel}");

        typeMetrics = EnrichWithSemanticAnalysis(typeMetrics, allTypes, allSyntaxTrees, compilationResult);

        var assemblyInfos = targets.Select(a =>
        {
            var types = allTypes.Where(t => t.Assembly == a.Name).ToList();
            var metrics = AssemblyMetrics.Compute(a.Name, types);
            var asmTypeMetrics = typeMetrics.Where(m => m.Assembly == a.Name).ToList();
            var health = CodeHealthCalculator.ComputeAssemblyHealth(asmTypeMetrics);
            return new AssemblyInfo(a.Name, a.Directory, a.References, metrics, health);
        }).ToList();

        var cycles = CycleDetector.DetectAll(deps, assemblyInfos);

        return new AnalysisResult(
            Path.GetFullPath(path),
            DateTimeOffset.UtcNow,
            assemblyInfos,
            allTypes,
            deps,
            typeMetrics,
            analysisLevel,
            cycles.Count > 0 ? cycles : null);
    }

    static CsprojInfo? ResolveCsprojInfo(string projectRoot)
    {
        var csprojFiles = CsprojParser.DiscoverCsprojFiles(projectRoot);
        if (csprojFiles.Count == 0) return null;

        var allRefs = new List<string>();
        var allDefines = new List<string>();
        string? langVersion = null;
        foreach (var csproj in csprojFiles)
        {
            var info = CsprojParser.TryParse(csproj);
            if (info is null) continue;
            allRefs.AddRange(info.ReferencePaths);
            allDefines.AddRange(info.DefineConstants);
            langVersion ??= info.LangVersion;
        }

        if (allRefs.Count == 0 && allDefines.Count == 0) return null;

        Console.Error.WriteLine($"Found {csprojFiles.Count} .csproj file(s), {allRefs.Count} references, {allDefines.Count} defines");
        return new CsprojInfo(
            allRefs.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            [],
            allDefines.Distinct().ToList(),
            langVersion);
    }

    static IReadOnlyList<string> MergePreprocessorSymbols(string projectRoot, CsprojInfo? csprojInfo)
    {
        var symbols = UnityDllResolver.GetPreprocessorDefines(projectRoot);
        if (csprojInfo is not { DefineConstants.Count: > 0 }) return symbols;

        var merged = new List<string>(symbols);
        merged.AddRange(csprojInfo.DefineConstants);
        return merged.Distinct().ToList();
    }

    static (List<TypeNodeInfo> Types, List<SyntaxTree> Trees) CollectTypes(
        IReadOnlyList<AsmdefInfo> targets, IReadOnlyList<string> preprocessorSymbols)
    {
        var allTypes = new List<TypeNodeInfo>();
        var allTrees = new List<SyntaxTree>();
        foreach (var asm in targets)
        {
            var result = TypeAnalyzer.AnalyzeDirectoryWithTrees(asm.Directory, asm.Name, preprocessorSymbols);
            allTypes.AddRange(result.Types);
            allTrees.AddRange(result.SyntaxTrees);
        }
        return (allTypes, allTrees);
    }

    static string TypeKey(string ns, string name) =>
        string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

    static IReadOnlyList<TypeMetrics> EnrichWithSemanticAnalysis(
        IReadOnlyList<TypeMetrics> typeMetrics,
        IReadOnlyList<TypeNodeInfo> allTypes,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        CompilationResult compilationResult)
    {
        var treeByPath = BuildTreeLookup(compilationResult, syntaxTrees);

        var typeInfoByKey = new Dictionary<string, TypeNodeInfo>();
        foreach (var t in allTypes)
            typeInfoByKey.TryAdd(TypeKey(t.Namespace, t.Name), t);

        var typeDeclLookup = BuildTypeDeclLookup(allTypes, treeByPath);
        var modelCache = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

        var result = new TypeMetrics[typeMetrics.Count];
        Parallel.For(0, typeMetrics.Count, i =>
        {
            result[i] = EnrichSingleType(
                typeMetrics[i], typeDeclLookup, typeInfoByKey, compilationResult, modelCache);
        });

        return result;
    }

    static Dictionary<string, SyntaxTree> BuildTreeLookup(
        CompilationResult compilationResult,
        IReadOnlyList<SyntaxTree> syntaxTrees)
    {
        var treeByPath = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
        var sourceSet = compilationResult.Compilation?.SyntaxTrees ?? syntaxTrees;
        foreach (var tree in sourceSet)
        {
            if (!string.IsNullOrEmpty(tree.FilePath))
                treeByPath.TryAdd(Path.GetFullPath(tree.FilePath), tree);
        }
        return treeByPath;
    }

    static Dictionary<string, TypeDeclarationSyntax> BuildTypeDeclLookup(
        IReadOnlyList<TypeNodeInfo> allTypes,
        Dictionary<string, SyntaxTree> treeByPath)
    {
        var typeDeclLookup = new Dictionary<string, TypeDeclarationSyntax>();
        foreach (var type in allTypes)
        {
            if (type.Kind is "enum" or "delegate") continue;
            var normalizedPath = Path.GetFullPath(type.FilePath);
            if (!treeByPath.TryGetValue(normalizedPath, out var tree)) continue;

            var root = tree.GetRoot();
            var baseName = type.Name.Split('<')[0];
            var typeDecl = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(td => td.Identifier.Text == baseName);
            if (typeDecl is not null)
                typeDeclLookup.TryAdd(TypeKey(type.Namespace, type.Name), typeDecl);
        }
        return typeDeclLookup;
    }

    static TypeMetrics EnrichSingleType(
        TypeMetrics metrics,
        Dictionary<string, TypeDeclarationSyntax> typeDeclLookup,
        Dictionary<string, TypeNodeInfo> typeInfoByKey,
        CompilationResult compilationResult,
        ConcurrentDictionary<SyntaxTree, SemanticModel> modelCache)
    {
        var key = TypeKey(metrics.Namespace, metrics.TypeName);
        var current = metrics;

        double? lcom = null;
        int? cbo = null;
        int? dit = null;
        if (typeDeclLookup.TryGetValue(key, out var typeDecl))
        {
            SemanticModel? model = null;
            if (compilationResult.Compilation is not null)
            {
                var tree = typeDecl.SyntaxTree;
                model = modelCache.GetOrAdd(tree, t => compilationResult.Compilation.GetSemanticModel(t));
            }

            lcom = LcomCalculator.Calculate(typeDecl, model);
            cbo = CboCalculator.Calculate(typeDecl, model);
            dit = DitCalculator.Calculate(typeDecl, model);

            if (model is not null)
                current = RecalculateCycCC(current, typeDecl, model);
        }

        typeInfoByKey.TryGetValue(key, out var typeInfo);
        var smells = typeInfo is not null
            ? CodeSmellDetector.Detect(current, typeInfo, lcom, cbo, dit)
            : [];

        return current with
        {
            Lcom = lcom,
            Cbo = cbo,
            Dit = dit,
            CodeSmells = smells.Count > 0 ? smells : null
        };
    }

    static TypeMetrics RecalculateCycCC(
        TypeMetrics metrics,
        TypeDeclarationSyntax typeDecl,
        SemanticModel model)
    {
        var methodDeclsByName = new Dictionary<string, MethodDeclarationSyntax>();
        foreach (var member in typeDecl.Members)
        {
            if (member is MethodDeclarationSyntax method)
                methodDeclsByName.TryAdd(method.Identifier.Text, method);
        }

        var anyChanged = false;
        var updatedMethods = new List<MethodMetrics>(metrics.Methods.Count);
        foreach (var mm in metrics.Methods)
        {
            if (methodDeclsByName.TryGetValue(mm.MethodName, out var methodDecl))
            {
                var body = (SyntaxNode?)methodDecl.Body ?? methodDecl.ExpressionBody;
                var newCycCC = CyclomaticComplexity.Calculate(body, model);
                if (newCycCC != mm.CyclomaticComplexity)
                {
                    anyChanged = true;
                    updatedMethods.Add(mm with { CyclomaticComplexity = newCycCC });
                    continue;
                }
            }
            updatedMethods.Add(mm);
        }

        if (!anyChanged) return metrics;

        var avgCycCC = updatedMethods.Count > 0
            ? Math.Round(updatedMethods.Average(m => (double)m.CyclomaticComplexity), 1)
            : 0.0;
        var maxCycCC = updatedMethods.Count > 0
            ? updatedMethods.Max(m => m.CyclomaticComplexity)
            : 0;

        return metrics with
        {
            Methods = updatedMethods,
            AverageCyclomaticComplexity = avgCycCC,
            MaxCyclomaticComplexity = maxCycCC
        };
    }

    static IReadOnlyList<TypeMetrics> EnrichWithCouplingMetrics(
        IReadOnlyList<TypeMetrics> typeMetrics,
        IReadOnlyDictionary<string, CouplingInfo> couplingMap)
    {
        var enriched = new List<TypeMetrics>(typeMetrics.Count);
        foreach (var metrics in typeMetrics)
        {
            if (couplingMap.TryGetValue(metrics.TypeName, out var coupling))
            {
                enriched.Add(metrics with
                {
                    AfferentCoupling = coupling.AfferentCoupling,
                    EfferentCoupling = coupling.EfferentCoupling,
                    Instability = coupling.Instability.HasValue ? Math.Round(coupling.Instability.Value, 2) : null
                });
            }
            else
            {
                enriched.Add(metrics);
            }
        }
        return enriched;
    }
}
