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
    ExpensiveUnityApiInHotPath,
    LinqInHotPath,
    CollectionAllocationInHotPath,
    StringConcatenationInHotPath
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
    int? Line = null);

public static class CodeSmellDetector
{
    public static IReadOnlyList<CodeSmell> Detect(
        TypeMetrics typeMetrics,
        TypeNodeInfo typeInfo,
        double? lcom,
        int? cbo = null,
        int? dit = null)
    {
        var smells = new List<CodeSmell>();

        DetectGodClass(typeMetrics, smells);
        DetectMethodSmells(typeMetrics, smells);
        DetectLowCohesion(typeMetrics, lcom, smells);
        DetectHighCoupling(typeMetrics, cbo, smells);
        DetectDeepInheritance(typeMetrics, dit, smells);

        return smells;
    }

    static void DetectGodClass(TypeMetrics metrics, List<CodeSmell> smells)
    {
        var byLines = metrics.LineCount >= SmellThresholds.GodClassLinesWarning;
        var byMethods = metrics.MethodCount >= SmellThresholds.GodClassMethodsWarning;
        if (!byLines && !byMethods) return;

        var severity = byLines && metrics.LineCount >= SmellThresholds.GodClassLinesCritical
            ? SmellSeverity.Critical
            : SmellSeverity.Warning;

        var message = (byLines, byMethods) switch
        {
            (true, true) => $"{metrics.LineCount} lines, {metrics.MethodCount} methods",
            (true, false) => $"{metrics.LineCount} lines (threshold: {SmellThresholds.GodClassLinesWarning})",
            _ => $"{metrics.MethodCount} methods (threshold: {SmellThresholds.GodClassMethodsWarning})"
        };

        smells.Add(new CodeSmell(CodeSmellKind.GodClass, severity, metrics.TypeName, null, message));
    }

    static void DetectMethodSmells(TypeMetrics metrics, List<CodeSmell> smells)
    {
        foreach (var method in metrics.Methods)
        {
            DetectLongMethod(metrics.TypeName, method, smells);
            DetectExcessiveParameters(metrics.TypeName, method, smells);
            DetectHighComplexity(metrics.TypeName, method, smells);
            DetectDeepNesting(metrics.TypeName, method, smells);
            DetectLowMaintainability(metrics.TypeName, method, smells);
        }
    }

    static void DetectLongMethod(string typeName, MethodMetrics method, List<CodeSmell> smells)
    {
        if (method.LineCount < SmellThresholds.LongMethodLinesWarning
            && method.CognitiveComplexity < SmellThresholds.LongMethodCogCcWarning)
            return;

        var severity = method.LineCount >= SmellThresholds.LongMethodLinesCritical
            || method.CognitiveComplexity >= SmellThresholds.LongMethodCogCcCritical
            ? SmellSeverity.Critical
            : SmellSeverity.Warning;

        var parts = new List<string>();
        if (method.LineCount >= SmellThresholds.LongMethodLinesWarning)
            parts.Add($"{method.LineCount} lines");
        if (method.CognitiveComplexity >= SmellThresholds.LongMethodCogCcWarning)
            parts.Add($"cognitive CC {method.CognitiveComplexity}");

        smells.Add(new CodeSmell(
            CodeSmellKind.LongMethod, severity, typeName, method.MethodName,
            string.Join(", ", parts)));
    }

    static void DetectExcessiveParameters(string typeName, MethodMetrics method, List<CodeSmell> smells)
    {
        if (method.ParameterCount <= SmellThresholds.ExcessiveParametersMax)
            return;

        smells.Add(new CodeSmell(
            CodeSmellKind.ExcessiveParameters, SmellSeverity.Warning,
            typeName, method.MethodName,
            $"{method.ParameterCount} parameters (threshold: {SmellThresholds.ExcessiveParametersMax})"));
    }

    static void DetectHighComplexity(string typeName, MethodMetrics method, List<CodeSmell> smells)
    {
        var parts = new List<string>();
        if (method.CyclomaticComplexity >= SmellThresholds.HighComplexityCycCcWarning)
            parts.Add($"cyclomatic CC {method.CyclomaticComplexity}");
        if (method.CognitiveComplexity >= SmellThresholds.HighComplexityCogCcWarning
            && method.CognitiveComplexity < SmellThresholds.LongMethodCogCcWarning)
            parts.Add($"cognitive CC {method.CognitiveComplexity}");

        if (parts.Count > 0)
        {
            smells.Add(new CodeSmell(
                CodeSmellKind.HighComplexity, SmellSeverity.Warning,
                typeName, method.MethodName,
                string.Join(", ", parts)));
        }
    }

    static void DetectDeepNesting(string typeName, MethodMetrics method, List<CodeSmell> smells)
    {
        if (method.MaxNestingDepth < SmellThresholds.DeepNestingDepthWarning)
            return;

        var severity = method.MaxNestingDepth >= SmellThresholds.DeepNestingDepthCritical
            ? SmellSeverity.Critical
            : SmellSeverity.Warning;
        smells.Add(new CodeSmell(
            CodeSmellKind.DeepNesting, severity, typeName, method.MethodName,
            $"nesting depth {method.MaxNestingDepth} (threshold: {SmellThresholds.DeepNestingDepthWarning})"));
    }

    static void DetectLowMaintainability(string typeName, MethodMetrics method, List<CodeSmell> smells)
    {
        if (method.MaintainabilityIndex is not < SmellThresholds.LowMaintainabilityMiWarning)
            return;

        smells.Add(new CodeSmell(
            CodeSmellKind.LowMaintainability, SmellSeverity.Warning,
            typeName, method.MethodName,
            $"MI {method.MaintainabilityIndex:F0} (threshold: {SmellThresholds.LowMaintainabilityMiWarning:0})"));
    }

    static void DetectLowCohesion(TypeMetrics metrics, double? lcom, List<CodeSmell> smells)
    {
        if (lcom is >= SmellThresholds.LowCohesionLcomWarning)
        {
            smells.Add(new CodeSmell(
                CodeSmellKind.LowCohesion, SmellSeverity.Warning,
                metrics.TypeName, null,
                $"LCOM {lcom:F2} (threshold: {SmellThresholds.LowCohesionLcomWarning:0.0})"));
        }
    }

    static void DetectHighCoupling(TypeMetrics metrics, int? cbo, List<CodeSmell> smells)
    {
        if (cbo is >= SmellThresholds.HighCouplingCboWarning)
        {
            var severity = cbo >= SmellThresholds.HighCouplingCboCritical
                ? SmellSeverity.Critical
                : SmellSeverity.Warning;
            smells.Add(new CodeSmell(
                CodeSmellKind.HighCoupling, severity,
                metrics.TypeName, null,
                $"CBO {cbo} (threshold: {SmellThresholds.HighCouplingCboWarning})"));
        }
    }

    static void DetectDeepInheritance(TypeMetrics metrics, int? dit, List<CodeSmell> smells)
    {
        if (dit is >= SmellThresholds.DeepInheritanceDitWarning)
        {
            smells.Add(new CodeSmell(
                CodeSmellKind.DeepInheritance, SmellSeverity.Warning,
                metrics.TypeName, null,
                $"DIT {dit} (threshold: {SmellThresholds.DeepInheritanceDitWarning})"));
        }
    }
}
