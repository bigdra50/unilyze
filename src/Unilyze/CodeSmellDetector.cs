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
    ThrowingSystemException
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
    string Message);

public static class CodeSmellDetector
{
    // Thresholds
    const int GodClassLines = 500;
    const int GodClassMethods = 20;
    const int LongMethodLines = 80;
    const int LongMethodCognitiveCC = 25;
    const int ExcessiveParamCount = 5;
    const int HighCyclomaticCC = 15;
    const int HighCognitiveCC = 15;
    const int DeepNestingDepth = 4;
    const double LowCohesionLcom = 0.8;
    const int HighCouplingCbo = 14;
    const double LowMaintainabilityMI = 20.0;
    const double CriticalMaintainabilityMI = 10.0;
    const int DeepInheritanceDit = 6;

    const int CriticalGodClassLines = 1000;
    const int CriticalLongMethodLines = 150;
    const int CriticalCognitiveCC = 40;
    const int CriticalNestingDepth = 6;
    const int CriticalCouplingCbo = 25;

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
        var byLines = metrics.LineCount >= GodClassLines;
        var byMethods = metrics.MethodCount >= GodClassMethods;
        if (!byLines && !byMethods) return;

        var severity = byLines && metrics.LineCount >= CriticalGodClassLines
            ? SmellSeverity.Critical
            : SmellSeverity.Warning;

        var message = (byLines, byMethods) switch
        {
            (true, true) => $"{metrics.LineCount} lines, {metrics.MethodCount} methods",
            (true, false) => $"{metrics.LineCount} lines (threshold: {GodClassLines})",
            _ => $"{metrics.MethodCount} methods (threshold: {GodClassMethods})"
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
        if (method.LineCount < LongMethodLines && method.CognitiveComplexity < LongMethodCognitiveCC)
            return;

        var severity = method.LineCount >= CriticalLongMethodLines || method.CognitiveComplexity >= CriticalCognitiveCC
            ? SmellSeverity.Critical
            : SmellSeverity.Warning;

        var parts = new List<string>();
        if (method.LineCount >= LongMethodLines)
            parts.Add($"{method.LineCount} lines");
        if (method.CognitiveComplexity >= LongMethodCognitiveCC)
            parts.Add($"cognitive CC {method.CognitiveComplexity}");

        smells.Add(new CodeSmell(
            CodeSmellKind.LongMethod, severity, typeName, method.MethodName,
            string.Join(", ", parts)));
    }

    static void DetectExcessiveParameters(string typeName, MethodMetrics method, List<CodeSmell> smells)
    {
        if (method.ParameterCount <= ExcessiveParamCount)
            return;

        smells.Add(new CodeSmell(
            CodeSmellKind.ExcessiveParameters, SmellSeverity.Warning,
            typeName, method.MethodName,
            $"{method.ParameterCount} parameters (threshold: {ExcessiveParamCount})"));
    }

    static void DetectHighComplexity(string typeName, MethodMetrics method, List<CodeSmell> smells)
    {
        var parts = new List<string>();
        if (method.CyclomaticComplexity >= HighCyclomaticCC)
            parts.Add($"cyclomatic CC {method.CyclomaticComplexity}");
        if (method.CognitiveComplexity >= HighCognitiveCC && method.CognitiveComplexity < LongMethodCognitiveCC)
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
        if (method.MaxNestingDepth < DeepNestingDepth)
            return;

        var severity = method.MaxNestingDepth >= CriticalNestingDepth
            ? SmellSeverity.Critical
            : SmellSeverity.Warning;
        smells.Add(new CodeSmell(
            CodeSmellKind.DeepNesting, severity, typeName, method.MethodName,
            $"nesting depth {method.MaxNestingDepth} (threshold: {DeepNestingDepth})"));
    }

    static void DetectLowMaintainability(string typeName, MethodMetrics method, List<CodeSmell> smells)
    {
        if (method.MaintainabilityIndex is not < LowMaintainabilityMI)
            return;

        var severity = method.MaintainabilityIndex < CriticalMaintainabilityMI
            ? SmellSeverity.Critical
            : SmellSeverity.Warning;
        smells.Add(new CodeSmell(
            CodeSmellKind.LowMaintainability, severity,
            typeName, method.MethodName,
            $"MI {method.MaintainabilityIndex:F0} (threshold: {LowMaintainabilityMI})"));
    }

    static void DetectLowCohesion(TypeMetrics metrics, double? lcom, List<CodeSmell> smells)
    {
        if (lcom is >= LowCohesionLcom)
        {
            smells.Add(new CodeSmell(
                CodeSmellKind.LowCohesion, SmellSeverity.Warning,
                metrics.TypeName, null,
                $"LCOM {lcom:F2} (threshold: {LowCohesionLcom})"));
        }
    }

    static void DetectHighCoupling(TypeMetrics metrics, int? cbo, List<CodeSmell> smells)
    {
        if (cbo is >= HighCouplingCbo)
        {
            var severity = cbo >= CriticalCouplingCbo
                ? SmellSeverity.Critical
                : SmellSeverity.Warning;
            smells.Add(new CodeSmell(
                CodeSmellKind.HighCoupling, severity,
                metrics.TypeName, null,
                $"CBO {cbo} (threshold: {HighCouplingCbo})"));
        }
    }

    static void DetectDeepInheritance(TypeMetrics metrics, int? dit, List<CodeSmell> smells)
    {
        if (dit is >= DeepInheritanceDit)
        {
            smells.Add(new CodeSmell(
                CodeSmellKind.DeepInheritance, SmellSeverity.Warning,
                metrics.TypeName, null,
                $"DIT {dit} (threshold: {DeepInheritanceDit})"));
        }
    }
}
