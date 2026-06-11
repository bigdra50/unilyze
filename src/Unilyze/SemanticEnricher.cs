using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

internal static class SemanticEnricher
{
    // Test-only hooks: simulate Roslyn-internal failures for fallback-path coverage.
    internal static Func<TypeDeclarationSyntax, bool>? TestSimulateRoslynFailureInCohesion;
    internal static Func<TypeDeclarationSyntax, bool>? TestSimulateRoslynFailureInFeatureDetect;

    readonly record struct CohesionMetrics(double? Lcom, int? Cbo, int? Dit, int? Rfc);

    static readonly IReadOnlySet<CodeSmellKind> NoDisabledRules = new HashSet<CodeSmellKind>();

    private sealed record EnrichmentContext(
        Dictionary<string, TypeDeclarationSyntax> TypeDeclLookup,
        Dictionary<string, TypeNodeInfo> TypeInfoByKey,
        CompilationResult CompilationResult,
        ConcurrentDictionary<SyntaxTree, SemanticModel> ModelCache,
        string Profile,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? SmellOverrides,
        IReadOnlySet<CodeSmellKind> InformationalSmellKinds,
        IReadOnlySet<CodeSmellKind> DisabledRuleKinds)
    {
        public static EnrichmentContext Create(
            IReadOnlyList<TypeNodeInfo> allTypes,
            IReadOnlyList<SyntaxTree> syntaxTrees,
            CompilationResult compilationResult,
            string profile,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? smellOverrides,
            IReadOnlySet<CodeSmellKind>? informationalSmellKinds,
            IReadOnlySet<CodeSmellKind>? disabledRuleKinds,
            int? maxParallelism = null)
        {
            profile = SmellThresholdProfiles.NormalizeProfile(profile);
            smellOverrides ??= null;
            informationalSmellKinds ??= new HashSet<CodeSmellKind>();
            disabledRuleKinds ??= NoDisabledRules;
            var treeByPath = SyntaxLookups.BuildTreeLookup(compilationResult, syntaxTrees);

            var typeInfoByKey = new Dictionary<string, TypeNodeInfo>();
            foreach (var t in allTypes)
                typeInfoByKey.TryAdd(TypeIdentity.GetTypeId(t), t);

            var typeDeclLookup = SyntaxLookups.BuildTypeDeclLookup(allTypes, treeByPath);
            var modelCache = new ConcurrentDictionary<SyntaxTree, SemanticModel>();
            PrewarmModelCache(compilationResult, typeDeclLookup, modelCache, maxParallelism);

            return new EnrichmentContext(
                typeDeclLookup, typeInfoByKey, compilationResult, modelCache,
                profile, smellOverrides, informationalSmellKinds, disabledRuleKinds);
        }

        static void PrewarmModelCache(
            CompilationResult compilationResult,
            Dictionary<string, TypeDeclarationSyntax> typeDeclLookup,
            ConcurrentDictionary<SyntaxTree, SemanticModel> modelCache,
            int? maxParallelism)
        {
            if (compilationResult.Compilation is null)
                return;

            var uniqueTrees = typeDeclLookup.Values.Select(td => td.SyntaxTree).Distinct().ToList();
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = UnilyzeConfig.ResolveMaxParallelism(maxParallelism)
            };
            Parallel.ForEach(uniqueTrees, parallelOptions, tree =>
            {
                modelCache.GetOrAdd(tree, static (t, c) => c.GetSemanticModel(t), compilationResult.Compilation);
            });
        }
    }

    public static IReadOnlyList<TypeMetrics> Enrich(
        IReadOnlyList<TypeMetrics> typeMetrics,
        IReadOnlyList<TypeNodeInfo> allTypes,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        CompilationResult compilationResult,
        string profile = SmellThresholdProfiles.DefaultProfileName,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? smellOverrides = null,
        IReadOnlySet<CodeSmellKind>? informationalSmellKinds = null,
        IReadOnlySet<CodeSmellKind>? disabledRuleKinds = null,
        int? maxParallelism = null)
    {
        var context = EnrichmentContext.Create(
            allTypes, syntaxTrees, compilationResult, profile, smellOverrides,
            informationalSmellKinds, disabledRuleKinds, maxParallelism);

        var result = new TypeMetrics[typeMetrics.Count];
        Parallel.For(0, typeMetrics.Count, i =>
        {
            result[i] = EnrichSingleType(typeMetrics[i], context);
        });

        return result;
    }

    static TypeMetrics EnrichSingleType(TypeMetrics metrics, EnrichmentContext context)
    {
        var key = TypeIdentity.GetTypeId(metrics);
        context.TypeInfoByKey.TryGetValue(key, out var typeInfo);

        SemanticModel? model = null;
        TypeDeclarationSyntax? typeDecl = null;
        if (context.TypeDeclLookup.TryGetValue(key, out typeDecl)
            && context.CompilationResult.Compilation is not null)
        {
            model = context.ModelCache.GetOrAdd(
                typeDecl.SyntaxTree,
                static (t, c) => c.GetSemanticModel(t),
                context.CompilationResult.Compilation);
        }

        var role = typeInfo is not null
            ? UnityContextClassifier.ClassifyRole(typeInfo, typeDecl, model)
            : TypeRole.PlainCSharp;
        var thresholds = SmellThresholdProfiles.ResolveEffectiveThresholds(
            context.Profile, role, context.SmellOverrides);

        var current = typeDecl is not null
            ? CohesionEnrichment.Apply(metrics, typeDecl, typeInfo, context)
            : metrics;

        var smells = typeInfo is not null
            ? CodeSmellDetector.Detect(current, typeInfo, current.Lcom, current.Cbo, current.Dit, thresholds).ToList()
            : new List<CodeSmell>();

        var informationalCount = ApplyInformationalSmells(
            ref smells, current, thresholds, context.InformationalSmellKinds);

        var wmc = WmcCalculator.Calculate(typeInfo?.Members ?? []);
        var detected = FeatureDetection.Run(metrics, context);
        smells = SmellFiltering.Apply(smells, context.DisabledRuleKinds);
        detected = SmellFiltering.Apply(detected, context.DisabledRuleKinds);

        var suppressionIndex = typeDecl is not null
            ? SuppressionIndex.Build(typeDecl)
            : SuppressionIndex.Empty;
        smells = ApplyInlineSuppressionToMetricSmells(smells, suppressionIndex);

        return StampEnrichedMetrics(current, smells, detected, suppressionIndex, wmc, informationalCount);
    }

    static int ApplyInformationalSmells(
        ref List<CodeSmell> smells,
        TypeMetrics metrics,
        EffectiveSmellThresholds thresholds,
        IReadOnlySet<CodeSmellKind> informationalKinds)
    {
        if (informationalKinds.Count == 0)
            return 0;

        var count = 0;
        if (informationalKinds.Contains(CodeSmellKind.LowCohesion)
            && metrics.Lcom is { } lcom
            && lcom >= thresholds.LowCohesionLcomWarning)
        {
            count++;
            smells = smells.Where(s => s.Kind != CodeSmellKind.LowCohesion).ToList();
        }

        return count;
    }

    static List<CodeSmell> ApplyInlineSuppressionToMetricSmells(
        IReadOnlyList<CodeSmell> smells,
        SuppressionIndex suppressionIndex)
    {
        if (smells.Count == 0)
            return smells is List<CodeSmell> list ? list : smells.ToList();

        var updated = new List<CodeSmell>(smells.Count);
        foreach (var smell in smells)
        {
            if (suppressionIndex.IsMetricSmellSuppressed(smell, out var justification))
                updated.Add(smell with { Suppressed = true, SuppressionJustification = justification });
            else
                updated.Add(smell);
        }

        return updated;
    }

    static TypeMetrics StampEnrichedMetrics(
        TypeMetrics current,
        IReadOnlyList<CodeSmell> smells,
        IReadOnlyList<DetectedSmell> detected,
        SuppressionIndex suppressionIndex,
        int wmc,
        int informationalCount)
    {
        var allSmells = SmellMerging.Convert(smells, detected, suppressionIndex);
        var activeDetected = detected.Where(d => !suppressionIndex.IsDetectorSmellSuppressed(d, out _)).ToList();
        var boxingCount = SmellMerging.CountByKind(activeDetected, CodeSmellKind.BoxingAllocation);
        var closureCount = SmellMerging.CountByKind(activeDetected, CodeSmellKind.ClosureCapture);
        var paramsCount = SmellMerging.CountByKind(activeDetected, CodeSmellKind.ParamsArrayAllocation);

        return current with
        {
            Wmc = wmc,
            BoxingCount = boxingCount > 0 ? boxingCount : null,
            ClosureCaptureCount = closureCount > 0 ? closureCount : null,
            ParamsAllocationCount = paramsCount > 0 ? paramsCount : null,
            CodeSmells = allSmells.Count > 0 ? allSmells : null,
            InformationalCount = informationalCount > 0 ? informationalCount : null
        };
    }

    static class CohesionEnrichment
    {
        public static TypeMetrics Apply(
            TypeMetrics metrics,
            TypeDeclarationSyntax typeDecl,
            TypeNodeInfo? typeInfo,
            EnrichmentContext context)
        {
            var cohesion = Calculate(typeDecl, typeInfo, context, out var cohesionModel);

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

        static CohesionMetrics Calculate(
            TypeDeclarationSyntax typeDecl,
            TypeNodeInfo? typeInfo,
            EnrichmentContext context,
            out SemanticModel? model)
        {
            model = null;
            double? lcom = null;
            int? cbo = null;
            int? dit = null;
            int? rfc = null;

            if (context.CompilationResult.Compilation is not null)
            {
                var tree = typeDecl.SyntaxTree;
                model = context.ModelCache.GetOrAdd(
                    tree, static (t, c) => c.GetSemanticModel(t), context.CompilationResult.Compilation);
            }

            try
            {
                if (TestSimulateRoslynFailureInCohesion?.Invoke(typeDecl) == true)
                    throw new NullReferenceException("Simulated Roslyn internal error");

                lcom = LcomCalculator.Calculate(typeDecl, model);
                cbo = CboCalculator.Calculate(typeDecl, model);
                rfc = RfcCalculator.Calculate(typeDecl, model);

                if (model is not null)
                    dit = DitCalculator.Calculate(typeDecl, model);
            }
            catch (Exception)
            {
                lcom = LcomCalculator.Calculate(typeDecl, model: null);
                cbo = CboCalculator.Calculate(typeDecl, model: null);
                rfc = RfcCalculator.Calculate(typeDecl, model: null);
                model = null;
            }

            dit ??= ResolveDitFallback(typeInfo, typeDecl);
            return new CohesionMetrics(lcom, cbo, dit, rfc);
        }

        static int ResolveDitFallback(TypeNodeInfo? typeInfo, TypeDeclarationSyntax typeDecl)
        {
            if (typeInfo is null)
                return DitCalculator.Calculate(typeDecl, model: null);
            if (typeInfo.Kind is "interface" or "struct" or "record struct")
                return 0;
            return typeInfo.BaseType != null ? 1 : 0;
        }

        static TypeMetrics RecalculateCycCC(
            TypeMetrics metrics,
            TypeDeclarationSyntax typeDecl,
            SemanticModel model)
        {
            var methodDeclsByName = BuildMethodDeclLookup(typeDecl);
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

        static Dictionary<string, MethodDeclarationSyntax> BuildMethodDeclLookup(TypeDeclarationSyntax typeDecl)
        {
            var methodDeclsByName = new Dictionary<string, MethodDeclarationSyntax>();
            foreach (var member in typeDecl.Members)
            {
                if (member is MethodDeclarationSyntax method)
                    methodDeclsByName.TryAdd(method.Identifier.Text, method);
            }
            return methodDeclsByName;
        }

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

    static class FeatureDetection
    {
        public static IReadOnlyList<DetectedSmell> Run(TypeMetrics metrics, EnrichmentContext context)
        {
            if (!context.TypeDeclLookup.TryGetValue(TypeIdentity.GetTypeId(metrics), out var td))
                return [];

            SemanticModel? mdl = null;
            if (context.CompilationResult.Compilation is not null)
            {
                mdl = context.ModelCache.GetOrAdd(
                    td.SyntaxTree, static (t, c) => c.GetSemanticModel(t), context.CompilationResult.Compilation);
            }

            try
            {
                if (TestSimulateRoslynFailureInFeatureDetect?.Invoke(td) == true)
                    throw new NullReferenceException("Simulated Roslyn internal error");

                var detected = RunAllDetectors(td, mdl);
                var unityContext = UnityContextClassifier.Classify(td, mdl);
                return ApplyHotPathEscalation(detected, unityContext);
            }
            catch (Exception)
            {
                return [];
            }
        }

        static List<DetectedSmell> RunAllDetectors(TypeDeclarationSyntax td, SemanticModel? mdl)
        {
            var detected = new List<DetectedSmell>();
            foreach (var detector in SmellDetectorRegistry.All)
                detected.AddRange(detector.Detect(td, mdl));
            return detected;
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
    }

    static class SmellFiltering
    {
        public static List<CodeSmell> Apply(
            IReadOnlyList<CodeSmell> smells, IReadOnlySet<CodeSmellKind> disabledRuleKinds)
        {
            if (disabledRuleKinds.Count == 0)
                return smells is List<CodeSmell> list ? list : smells.ToList();

            return smells.Where(s => !disabledRuleKinds.Contains(s.Kind)).ToList();
        }

        public static List<DetectedSmell> Apply(
            IReadOnlyList<DetectedSmell> detected, IReadOnlySet<CodeSmellKind> disabledRuleKinds)
        {
            if (disabledRuleKinds.Count == 0)
                return detected is List<DetectedSmell> list ? list : detected.ToList();

            return detected.Where(d => !disabledRuleKinds.Contains(d.Kind)).ToList();
        }
    }

    static class SmellMerging
    {
        public static List<CodeSmell> Convert(
            IReadOnlyList<CodeSmell> baseSmells,
            IReadOnlyList<DetectedSmell> detected,
            SuppressionIndex suppressionIndex)
        {
            var allSmells = new List<CodeSmell>(baseSmells.Count + detected.Count);
            allSmells.AddRange(baseSmells);
            foreach (var d in detected)
            {
                var suppressed = suppressionIndex.IsDetectorSmellSuppressed(d, out var justification);
                allSmells.Add(new CodeSmell(
                    d.Kind, d.Severity, d.TypeName, d.MethodName, d.Message, d.Line,
                    Suppressed: suppressed ? true : null,
                    SuppressionJustification: justification));
            }
            return allSmells;
        }

        public static int CountByKind(IReadOnlyList<DetectedSmell> detected, CodeSmellKind kind)
            => detected.Count(d => d.Kind == kind);
    }
}
