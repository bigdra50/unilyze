using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

internal static class SemanticEnricher
{
    readonly record struct CohesionMetrics(double? Lcom, int? Cbo, int? Dit, int? Rfc);

    sealed record DetectorResults(
        IReadOnlyList<BoxingOccurrence> Boxings,
        IReadOnlyList<ClosureCapture> Closures,
        IReadOnlyList<ParamsAllocation> ParamsAllocs,
        ExceptionFlowResult? ExceptionFlow);

    internal static IReadOnlyList<TypeNodeInfo> ResolveTypeRelationships(
        IReadOnlyList<TypeNodeInfo> allTypes,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        CompilationResult compilationResult)
    {
        if (compilationResult.Compilation is null)
            return allTypes;

        var treeByPath = BuildTreeLookup(compilationResult, syntaxTrees);
        var typeDeclLookup = BuildTypeDeclLookup(allTypes, treeByPath);
        var modelCache = new ConcurrentDictionary<SyntaxTree, SemanticModel>();
        var resolved = new List<TypeNodeInfo>(allTypes.Count);

        foreach (var type in allTypes)
        {
            if (type.Kind is "enum" or "delegate")
            {
                resolved.Add(type);
                continue;
            }

            if (!typeDeclLookup.TryGetValue(TypeIdentity.GetTypeId(type), out var typeDecl))
            {
                resolved.Add(type);
                continue;
            }

            var model = modelCache.GetOrAdd(typeDecl.SyntaxTree, t => compilationResult.Compilation.GetSemanticModel(t));
            resolved.Add(ResolveExplicitBaseList(type, typeDecl, model));
        }

        return resolved;
    }

    public static IReadOnlyList<TypeMetrics> Enrich(
        IReadOnlyList<TypeMetrics> typeMetrics,
        IReadOnlyList<TypeNodeInfo> allTypes,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        CompilationResult compilationResult)
    {
        var treeByPath = BuildTreeLookup(compilationResult, syntaxTrees);

        var typeInfoByKey = new Dictionary<string, TypeNodeInfo>();
        foreach (var t in allTypes)
            typeInfoByKey.TryAdd(TypeIdentity.GetTypeId(t), t);

        var typeDeclLookup = BuildTypeDeclLookup(allTypes, treeByPath);
        var modelCache = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

        // Pre-warm SemanticModel cache: distribute initial GetSemanticModel cost across threads
        if (compilationResult.Compilation is not null)
        {
            var uniqueTrees = typeDeclLookup.Values.Select(td => td.SyntaxTree).Distinct().ToList();
            Parallel.ForEach(uniqueTrees, tree =>
            {
                modelCache.GetOrAdd(tree, t => compilationResult.Compilation.GetSemanticModel(t));
            });
        }

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
            var typeDecl = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(td => TypeIdentity.CreateTypeId(td, type.Assembly) == TypeIdentity.GetTypeId(type));
            if (typeDecl is not null)
                typeDeclLookup.TryAdd(TypeIdentity.GetTypeId(type), typeDecl);
        }
        return typeDeclLookup;
    }

    static TypeNodeInfo ResolveExplicitBaseList(
        TypeNodeInfo type,
        TypeDeclarationSyntax typeDecl,
        SemanticModel model)
    {
        if (typeDecl.BaseList is null)
            return type;

        string? baseType = null;
        var interfaces = new List<string>();

        foreach (var baseTypeSyntax in typeDecl.BaseList.Types)
        {
            var typeSymbol = model.GetTypeInfo(baseTypeSyntax.Type).Type as INamedTypeSymbol;
            var displayName = typeSymbol?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                ?? baseTypeSyntax.Type.ToString();

            if (type.Kind == "interface" || typeSymbol?.TypeKind == TypeKind.Interface)
            {
                interfaces.Add(displayName);
                continue;
            }

            baseType ??= displayName;
        }

        return type with
        {
            BaseType = type.Kind == "interface" ? null : baseType,
            Interfaces = interfaces.Distinct().ToList()
        };
    }

    static TypeMetrics EnrichSingleType(
        TypeMetrics metrics,
        Dictionary<string, TypeDeclarationSyntax> typeDeclLookup,
        Dictionary<string, TypeNodeInfo> typeInfoByKey,
        CompilationResult compilationResult,
        ConcurrentDictionary<SyntaxTree, SemanticModel> modelCache)
    {
        var key = TypeIdentity.GetTypeId(metrics);
        var current = metrics;

        typeInfoByKey.TryGetValue(key, out var typeInfo);

        SemanticModel? cohesionModel = null;
        if (typeDeclLookup.TryGetValue(key, out var typeDecl))
        {
            var cohesion = CalculateCohesionMetrics(
                typeDecl, typeInfo, compilationResult, modelCache, out cohesionModel);
            current = cohesionModel is not null
                ? RecalculateCycCC(current, typeDecl, cohesionModel)
                : current;

            current = current with
            {
                Lcom = cohesion.Lcom,
                Cbo = cohesion.Cbo,
                Dit = cohesion.Dit,
                Rfc = cohesion.Rfc,
            };
        }

        var smells = typeInfo is not null
            ? CodeSmellDetector.Detect(current, typeInfo, current.Lcom, current.Cbo, current.Dit)
            : new List<CodeSmell>();

        var wmc = WmcCalculator.Calculate(typeInfo?.Members ?? []);

        var detectorResults = RunFeatureDetectors(metrics, typeDeclLookup, compilationResult, modelCache);
        var allSmells = ConvertDetectorResultsToSmells(smells, metrics, detectorResults);

        return current with
        {
            Wmc = wmc,
            BoxingCount = detectorResults.Boxings.Count > 0 ? detectorResults.Boxings.Count : null,
            ClosureCaptureCount = detectorResults.Closures.Count > 0 ? detectorResults.Closures.Count : null,
            ParamsAllocationCount = detectorResults.ParamsAllocs.Count > 0 ? detectorResults.ParamsAllocs.Count : null,
            CodeSmells = allSmells.Count > 0 ? allSmells : null
        };
    }

    static CohesionMetrics CalculateCohesionMetrics(
        TypeDeclarationSyntax typeDecl,
        TypeNodeInfo? typeInfo,
        CompilationResult compilationResult,
        ConcurrentDictionary<SyntaxTree, SemanticModel> modelCache,
        out SemanticModel? model)
    {
        model = null;
        double? lcom = null;
        int? cbo = null;
        int? dit = null;
        int? rfc = null;

        if (compilationResult.Compilation is not null)
        {
            var tree = typeDecl.SyntaxTree;
            model = modelCache.GetOrAdd(tree, t => compilationResult.Compilation.GetSemanticModel(t));
        }

        try
        {
            lcom = LcomCalculator.Calculate(typeDecl, model);
            cbo = CboCalculator.Calculate(typeDecl, model);
            rfc = RfcCalculator.Calculate(typeDecl, model);

            if (model is not null)
            {
                dit = DitCalculator.Calculate(typeDecl, model);
            }
        }
        catch (Exception)
        {
            // Roslyn internal errors (e.g. NullableWalker NRE) — fall back to syntactic analysis
            lcom = LcomCalculator.Calculate(typeDecl, model: null);
            cbo = CboCalculator.Calculate(typeDecl, model: null);
            rfc = RfcCalculator.Calculate(typeDecl, model: null);
            model = null;
        }

        dit ??= ResolveDitFallback(typeInfo, typeDecl);

        return new CohesionMetrics(lcom, cbo, dit, rfc);
    }

    // Syntactic DIT when the semantic calculation is unavailable.
    static int ResolveDitFallback(TypeNodeInfo? typeInfo, TypeDeclarationSyntax typeDecl)
    {
        if (typeInfo is null)
            return DitCalculator.Calculate(typeDecl, model: null);
        if (typeInfo.Kind is "interface" or "struct" or "record struct")
            return 0;
        return typeInfo.BaseType != null ? 1 : 0;
    }

    static DetectorResults RunFeatureDetectors(
        TypeMetrics metrics,
        Dictionary<string, TypeDeclarationSyntax> typeDeclLookup,
        CompilationResult compilationResult,
        ConcurrentDictionary<SyntaxTree, SemanticModel> modelCache)
    {
        IReadOnlyList<BoxingOccurrence> boxings = [];
        IReadOnlyList<ClosureCapture> closures = [];
        IReadOnlyList<ParamsAllocation> paramsAllocs = [];
        ExceptionFlowResult? exceptionFlow = null;

        if (typeDeclLookup.TryGetValue(TypeIdentity.GetTypeId(metrics), out var td))
        {
            SemanticModel? mdl = null;
            if (compilationResult.Compilation is not null)
                mdl = modelCache.GetOrAdd(td.SyntaxTree, t => compilationResult.Compilation.GetSemanticModel(t));

            try
            {
                boxings = BoxingDetector.Detect(td, mdl);
                closures = ClosureDetector.Detect(td, mdl);
                paramsAllocs = ParamsArrayDetector.Detect(td, mdl);
                exceptionFlow = ExceptionFlowAnalyzer.Analyze(td, mdl);
            }
            catch (Exception)
            {
                // Roslyn internal errors — graceful degradation
            }
        }

        return new DetectorResults(boxings, closures, paramsAllocs, exceptionFlow);
    }

    static List<CodeSmell> ConvertDetectorResultsToSmells(
        IReadOnlyList<CodeSmell> baseSmells,
        TypeMetrics metrics,
        DetectorResults detectorResults)
    {
        var allSmells = new List<CodeSmell>(baseSmells);
        foreach (var b in detectorResults.Boxings)
            allSmells.Add(new CodeSmell(CodeSmellKind.BoxingAllocation, SmellSeverity.Warning, metrics.TypeName, b.MethodName, b.Description));
        foreach (var c in detectorResults.Closures)
            allSmells.Add(new CodeSmell(CodeSmellKind.ClosureCapture, SmellSeverity.Warning, metrics.TypeName, c.MethodName, $"captures: {string.Join(", ", c.CapturedVariables)}"));
        foreach (var p in detectorResults.ParamsAllocs)
            allSmells.Add(new CodeSmell(CodeSmellKind.ParamsArrayAllocation, SmellSeverity.Warning, metrics.TypeName, p.MethodName, $"params call to {p.CalledMethod} ({p.ArgCount} args)"));
        if (detectorResults.ExceptionFlow is not null)
            AddExceptionFlowSmells(allSmells, metrics.TypeName, detectorResults.ExceptionFlow);
        return allSmells;
    }

    static void AddExceptionFlowSmells(List<CodeSmell> smells, string typeName, ExceptionFlowResult flow)
    {
        foreach (var ca in flow.CatchAllClauses.Where(c => !c.HasRethrow))
            smells.Add(new CodeSmell(CodeSmellKind.CatchAllException, SmellSeverity.Warning, typeName, ca.MethodName, $"catch-all at line {ca.Line}"));
        foreach (var mi in flow.MissingInnerExceptions)
            smells.Add(new CodeSmell(CodeSmellKind.MissingInnerException, SmellSeverity.Warning, typeName, mi.MethodName, $"throw new {mi.NewExceptionType} without inner exception"));
        foreach (var se in flow.SystemExceptionThrows)
            smells.Add(new CodeSmell(CodeSmellKind.ThrowingSystemException, SmellSeverity.Warning, typeName, se.MethodName, "throw new Exception() directly"));
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
            var updated = RecalculateMethodCycCC(mm, methodDeclsByName, model);
            anyChanged |= !ReferenceEquals(updated, mm);
            updatedMethods.Add(updated);
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

    // Returns the original instance when the semantic CycCC matches the syntactic one.
    static MethodMetrics RecalculateMethodCycCC(
        MethodMetrics mm,
        Dictionary<string, MethodDeclarationSyntax> methodDeclsByName,
        SemanticModel model)
    {
        if (!methodDeclsByName.TryGetValue(mm.MethodName, out var methodDecl))
            return mm;

        var body = (SyntaxNode?)methodDecl.Body ?? methodDecl.ExpressionBody;
        var newCycCC = CyclomaticComplexity.Calculate(body, model);
        return newCycCC == mm.CyclomaticComplexity ? mm : mm with { CyclomaticComplexity = newCycCC };
    }
}
