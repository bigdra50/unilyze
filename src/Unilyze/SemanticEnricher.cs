using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

internal static class SemanticEnricher
{
    // Test-only hooks: simulate Roslyn-internal failures for fallback-path coverage.
    internal static Func<TypeDeclarationSyntax, bool>? TestSimulateRoslynFailureInCohesion;
    internal static Func<TypeDeclarationSyntax, bool>? TestSimulateRoslynFailureInFeatureDetect;

    readonly record struct CohesionMetrics(double? Lcom, int? Cbo, int? Dit, int? Rfc);

    public static IReadOnlyList<TypeMetrics> Enrich(
        IReadOnlyList<TypeMetrics> typeMetrics,
        IReadOnlyList<TypeNodeInfo> allTypes,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        CompilationResult compilationResult)
    {
        var treeByPath = SyntaxLookups.BuildTreeLookup(compilationResult, syntaxTrees);

        var typeInfoByKey = new Dictionary<string, TypeNodeInfo>();
        foreach (var t in allTypes)
            typeInfoByKey.TryAdd(TypeIdentity.GetTypeId(t), t);

        var typeDeclLookup = SyntaxLookups.BuildTypeDeclLookup(allTypes, treeByPath);
        var modelCache = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

        // Pre-warm SemanticModel cache: distribute initial GetSemanticModel cost across threads
        if (compilationResult.Compilation is not null)
        {
            var uniqueTrees = typeDeclLookup.Values.Select(td => td.SyntaxTree).Distinct().ToList();
            Parallel.ForEach(uniqueTrees, tree =>
            {
                modelCache.GetOrAdd(tree, static (t, c) => c.GetSemanticModel(t), compilationResult.Compilation);
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

    static TypeMetrics EnrichSingleType(
        TypeMetrics metrics,
        Dictionary<string, TypeDeclarationSyntax> typeDeclLookup,
        Dictionary<string, TypeNodeInfo> typeInfoByKey,
        CompilationResult compilationResult,
        ConcurrentDictionary<SyntaxTree, SemanticModel> modelCache)
    {
        var key = TypeIdentity.GetTypeId(metrics);

        typeInfoByKey.TryGetValue(key, out var typeInfo);

        var current = typeDeclLookup.TryGetValue(key, out var typeDecl)
            ? ApplyCohesionMetrics(metrics, typeDecl, typeInfo, compilationResult, modelCache)
            : metrics;

        var smells = typeInfo is not null
            ? CodeSmellDetector.Detect(current, typeInfo, current.Lcom, current.Cbo, current.Dit)
            : new List<CodeSmell>();

        var wmc = WmcCalculator.Calculate(typeInfo?.Members ?? []);

        var detected = RunFeatureDetectors(metrics, typeDeclLookup, compilationResult, modelCache);
        var allSmells = ConvertDetectedSmells(smells, detected);

        var boxingCount = CountByKind(detected, CodeSmellKind.BoxingAllocation);
        var closureCount = CountByKind(detected, CodeSmellKind.ClosureCapture);
        var paramsCount = CountByKind(detected, CodeSmellKind.ParamsArrayAllocation);

        return current with
        {
            Wmc = wmc,
            BoxingCount = boxingCount > 0 ? boxingCount : null,
            ClosureCaptureCount = closureCount > 0 ? closureCount : null,
            ParamsAllocationCount = paramsCount > 0 ? paramsCount : null,
            CodeSmells = allSmells.Count > 0 ? allSmells : null
        };
    }

    // Recalculates semantic CycCC and stamps LCOM/CBO/DIT/RFC onto the metrics record.
    static TypeMetrics ApplyCohesionMetrics(
        TypeMetrics metrics,
        TypeDeclarationSyntax typeDecl,
        TypeNodeInfo? typeInfo,
        CompilationResult compilationResult,
        ConcurrentDictionary<SyntaxTree, SemanticModel> modelCache)
    {
        var cohesion = CalculateCohesionMetrics(
            typeDecl, typeInfo, compilationResult, modelCache, out var cohesionModel);

        var current = cohesionModel is not null
            ? RecalculateCycCC(metrics, typeDecl, cohesionModel)
            : metrics;

        return current with
        {
            Lcom = cohesion.Lcom,
            Cbo = cohesion.Cbo,
            Dit = cohesion.Dit,
            Rfc = cohesion.Rfc,
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
            model = modelCache.GetOrAdd(tree, static (t, c) => c.GetSemanticModel(t), compilationResult.Compilation);
        }

        try
        {
            if (TestSimulateRoslynFailureInCohesion?.Invoke(typeDecl) == true)
                throw new NullReferenceException("Simulated Roslyn internal error");

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

    static IReadOnlyList<DetectedSmell> RunFeatureDetectors(
        TypeMetrics metrics,
        Dictionary<string, TypeDeclarationSyntax> typeDeclLookup,
        CompilationResult compilationResult,
        ConcurrentDictionary<SyntaxTree, SemanticModel> modelCache)
    {
        if (!typeDeclLookup.TryGetValue(TypeIdentity.GetTypeId(metrics), out var td))
            return [];

        SemanticModel? mdl = null;
        if (compilationResult.Compilation is not null)
            mdl = modelCache.GetOrAdd(td.SyntaxTree, static (t, c) => c.GetSemanticModel(t), compilationResult.Compilation);

        try
        {
            if (TestSimulateRoslynFailureInFeatureDetect?.Invoke(td) == true)
                throw new NullReferenceException("Simulated Roslyn internal error");

            var detected = new List<DetectedSmell>();
            foreach (var detector in SmellDetectorRegistry.All)
                detected.AddRange(detector.Detect(td, mdl));

            var unityContext = UnityContextClassifier.Classify(td, mdl);
            return ApplyHotPathEscalation(detected, unityContext);
        }
        catch (Exception)
        {
            // Roslyn internal errors — graceful degradation
            return [];
        }
    }

    static IReadOnlyList<DetectedSmell> ApplyHotPathEscalation(
        List<DetectedSmell> detected,
        UnityTypeContext context)
    {
        if (!context.IsMonoBehaviour)
            return detected;

        for (var i = 0; i < detected.Count; i++)
        {
            var smell = detected[i];
            if (!ShouldEscalateHotPathSmell(context, smell))
                continue;

            detected[i] = smell with { Severity = SmellSeverity.Critical };
        }

        return detected;
    }

    static bool ShouldEscalateHotPathSmell(UnityTypeContext context, DetectedSmell smell)
    {
        if (smell.MethodName is null || !context.HotPathMethodNames.Contains(smell.MethodName))
            return false;

        return smell.Kind is CodeSmellKind.BoxingAllocation
            or CodeSmellKind.ClosureCapture
            or CodeSmellKind.ParamsArrayAllocation;
    }

    static List<CodeSmell> ConvertDetectedSmells(
        IReadOnlyList<CodeSmell> baseSmells,
        IReadOnlyList<DetectedSmell> detected)
    {
        var allSmells = new List<CodeSmell>(baseSmells.Count + detected.Count);
        allSmells.AddRange(baseSmells);
        foreach (var d in detected)
        {
            allSmells.Add(new CodeSmell(
                d.Kind, d.Severity, d.TypeName, d.MethodName, d.Message, d.Line));
        }
        return allSmells;
    }

    static int CountByKind(IReadOnlyList<DetectedSmell> detected, CodeSmellKind kind)
        => detected.Count(d => d.Kind == kind);

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
