namespace Unilyze;

public enum CodeSmellKind
{
    GodClass,
    LongMethod,
    ExcessiveParameters,
    HighComplexity,
    DeepNesting,
    LowCohesion,
    HighCoupling,
    CyclicDependency,
    LowMaintainability,
    DeepInheritance,
    BoxingAllocation,
    ClosureCapture,
    ParamsArrayAllocation,
    CatchAllException,
    MissingInnerException,
    ThrowingSystemException,
    WeakTemporization,
    ExpensiveUnityApiInHotPath,
    LinqInHotPath,
    CollectionAllocationInHotPath,
    StringConcatenationInHotPath,
    AsyncVoidMethod,
    BlockingTaskWait
}

public enum SmellSeverity
{
    Warning,
    Critical
}

public sealed record CodeSmell(
    CodeSmellKind Kind,
    SmellSeverity Severity,
    string TypeName,
    string? MethodName,
    string Message,
    int? Line = null,
    bool? Baselined = null);

public static class CodeSmellDetector
{
    public static IReadOnlyList<CodeSmell> Detect(
        TypeMetrics typeMetrics,
        TypeNodeInfo typeInfo,
        double? lcom,
        int? cbo = null,
        int? dit = null,
        EffectiveSmellThresholds? thresholds = null)
    {
        thresholds ??= EffectiveSmellThresholds.Default;
        var smells = new List<CodeSmell>();

        DetectGodClass(typeMetrics, smells, thresholds);
        DetectMethodSmells(typeMetrics, smells, thresholds);
        DetectLowCohesion(typeMetrics, lcom, smells, thresholds);
        DetectHighCoupling(typeMetrics, cbo, smells, thresholds);
        DetectDeepInheritance(typeMetrics, dit, smells, thresholds);

        return smells;
    }

    static void DetectGodClass(TypeMetrics metrics, List<CodeSmell> smells, EffectiveSmellThresholds thresholds)
    {
        var byLines = metrics.LineCount >= thresholds.GodClassLinesWarning;
        var byMethods = metrics.MethodCount >= thresholds.GodClassMethodsWarning;
        if (!byLines && !byMethods) return;

        var severity = byLines && metrics.LineCount >= thresholds.GodClassLinesCritical
            ? SmellSeverity.Critical
            : SmellSeverity.Warning;

        var message = (byLines, byMethods) switch
        {
            (true, true) => $"{metrics.LineCount} lines, {metrics.MethodCount} methods",
            (true, false) => $"{metrics.LineCount} lines (threshold: {thresholds.GodClassLinesWarning})",
            _ => $"{metrics.MethodCount} methods (threshold: {thresholds.GodClassMethodsWarning})"
        };

        smells.Add(new CodeSmell(CodeSmellKind.GodClass, severity, metrics.TypeName, null, message));
    }

    static void DetectMethodSmells(TypeMetrics metrics, List<CodeSmell> smells, EffectiveSmellThresholds thresholds)
    {
        foreach (var method in metrics.Methods)
        {
            DetectLongMethod(metrics.TypeName, method, smells, thresholds);
            DetectExcessiveParameters(metrics.TypeName, method, smells, thresholds);
            DetectHighComplexity(metrics.TypeName, method, smells, thresholds);
            DetectDeepNesting(metrics.TypeName, method, smells, thresholds);
            DetectLowMaintainability(metrics.TypeName, method, smells, thresholds);
        }
    }

    static void DetectLongMethod(
        string typeName, MethodMetrics method, List<CodeSmell> smells, EffectiveSmellThresholds thresholds)
    {
        if (method.LineCount < thresholds.LongMethodLinesWarning
            && method.CognitiveComplexity < thresholds.LongMethodCogCcWarning)
            return;

        var severity = method.LineCount >= thresholds.LongMethodLinesCritical
            || method.CognitiveComplexity >= thresholds.LongMethodCogCcCritical
            ? SmellSeverity.Critical
            : SmellSeverity.Warning;

        var parts = new List<string>();
        if (method.LineCount >= thresholds.LongMethodLinesWarning)
            parts.Add($"{method.LineCount} lines");
        if (method.CognitiveComplexity >= thresholds.LongMethodCogCcWarning)
            parts.Add($"cognitive CC {method.CognitiveComplexity}");

        smells.Add(new CodeSmell(
            CodeSmellKind.LongMethod, severity, typeName, method.MethodName,
            string.Join(", ", parts)));
    }

    static void DetectExcessiveParameters(
        string typeName, MethodMetrics method, List<CodeSmell> smells, EffectiveSmellThresholds thresholds)
    {
        if (method.ParameterCount <= thresholds.ExcessiveParametersMax)
            return;

        smells.Add(new CodeSmell(
            CodeSmellKind.ExcessiveParameters, SmellSeverity.Warning,
            typeName, method.MethodName,
            $"{method.ParameterCount} parameters (threshold: {thresholds.ExcessiveParametersMax})"));
    }

    static void DetectHighComplexity(
        string typeName, MethodMetrics method, List<CodeSmell> smells, EffectiveSmellThresholds thresholds)
    {
        var parts = new List<string>();
        if (method.CyclomaticComplexity >= thresholds.HighComplexityCycCcWarning)
            parts.Add($"cyclomatic CC {method.CyclomaticComplexity}");
        if (method.CognitiveComplexity >= thresholds.HighComplexityCogCcWarning
            && method.CognitiveComplexity < thresholds.LongMethodCogCcWarning)
            parts.Add($"cognitive CC {method.CognitiveComplexity}");

        if (parts.Count > 0)
        {
            smells.Add(new CodeSmell(
                CodeSmellKind.HighComplexity, SmellSeverity.Warning,
                typeName, method.MethodName,
                string.Join(", ", parts)));
        }
    }

    static void DetectDeepNesting(
        string typeName, MethodMetrics method, List<CodeSmell> smells, EffectiveSmellThresholds thresholds)
    {
        if (method.MaxNestingDepth < thresholds.DeepNestingDepthWarning)
            return;

        var severity = method.MaxNestingDepth >= thresholds.DeepNestingDepthCritical
            ? SmellSeverity.Critical
            : SmellSeverity.Warning;
        smells.Add(new CodeSmell(
            CodeSmellKind.DeepNesting, severity, typeName, method.MethodName,
            $"nesting depth {method.MaxNestingDepth} (threshold: {thresholds.DeepNestingDepthWarning})"));
    }

    static void DetectLowMaintainability(
        string typeName, MethodMetrics method, List<CodeSmell> smells, EffectiveSmellThresholds thresholds)
    {
        if (method.MaintainabilityIndex is not { } mi || mi >= thresholds.LowMaintainabilityMiWarning)
            return;

        smells.Add(new CodeSmell(
            CodeSmellKind.LowMaintainability, SmellSeverity.Warning,
            typeName, method.MethodName,
            $"MI {method.MaintainabilityIndex:F0} (threshold: {thresholds.LowMaintainabilityMiWarning:0})"));
    }

    static void DetectLowCohesion(
        TypeMetrics metrics, double? lcom, List<CodeSmell> smells, EffectiveSmellThresholds thresholds)
    {
        if (lcom is { } l && l >= thresholds.LowCohesionLcomWarning)
        {
            smells.Add(new CodeSmell(
                CodeSmellKind.LowCohesion, SmellSeverity.Warning,
                metrics.TypeName, null,
                $"LCOM {lcom:F2} (threshold: {thresholds.LowCohesionLcomWarning:0.0})"));
        }
    }

    static void DetectHighCoupling(
        TypeMetrics metrics, int? cbo, List<CodeSmell> smells, EffectiveSmellThresholds thresholds)
    {
        if (cbo is { } c && c >= thresholds.HighCouplingCboWarning)
        {
            var severity = c >= thresholds.HighCouplingCboCritical
                ? SmellSeverity.Critical
                : SmellSeverity.Warning;
            smells.Add(new CodeSmell(
                CodeSmellKind.HighCoupling, severity,
                metrics.TypeName, null,
                $"CBO {cbo} (threshold: {thresholds.HighCouplingCboWarning})"));
        }
    }

    static void DetectDeepInheritance(
        TypeMetrics metrics, int? dit, List<CodeSmell> smells, EffectiveSmellThresholds thresholds)
    {
        if (dit is { } d && d >= thresholds.DeepInheritanceDitWarning)
        {
            smells.Add(new CodeSmell(
                CodeSmellKind.DeepInheritance, SmellSeverity.Warning,
                metrics.TypeName, null,
                $"DIT {dit} (threshold: {thresholds.DeepInheritanceDitWarning})"));
        }
    }
}
