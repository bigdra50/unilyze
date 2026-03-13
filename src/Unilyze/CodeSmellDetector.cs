namespace Unilyze;

public enum CodeSmellKind
{
    GodClass,
    LongMethod,
    ExcessiveParameters,
    HighComplexity,
    DeepNesting,
    LowCohesion
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

    const int CriticalGodClassLines = 1000;
    const int CriticalLongMethodLines = 150;
    const int CriticalCognitiveCC = 40;
    const int CriticalNestingDepth = 6;

    public static IReadOnlyList<CodeSmell> Detect(
        TypeMetrics typeMetrics,
        TypeNodeInfo typeInfo,
        double? lcom)
    {
        var smells = new List<CodeSmell>();

        DetectGodClass(typeMetrics, smells);
        DetectMethodSmells(typeMetrics, smells);
        DetectLowCohesion(typeMetrics, lcom, smells);

        return smells;
    }

    static void DetectGodClass(TypeMetrics metrics, List<CodeSmell> smells)
    {
        var isGodByLines = metrics.LineCount >= GodClassLines;
        var isGodByMethods = metrics.MethodCount >= GodClassMethods;

        if (isGodByLines && isGodByMethods)
        {
            var severity = metrics.LineCount >= CriticalGodClassLines
                ? SmellSeverity.Critical
                : SmellSeverity.Warning;
            smells.Add(new CodeSmell(
                CodeSmellKind.GodClass, severity, metrics.TypeName, null,
                $"{metrics.LineCount} lines, {metrics.MethodCount} methods"));
        }
        else if (isGodByLines)
        {
            smells.Add(new CodeSmell(
                CodeSmellKind.GodClass, SmellSeverity.Warning, metrics.TypeName, null,
                $"{metrics.LineCount} lines (threshold: {GodClassLines})"));
        }
        else if (isGodByMethods)
        {
            smells.Add(new CodeSmell(
                CodeSmellKind.GodClass, SmellSeverity.Warning, metrics.TypeName, null,
                $"{metrics.MethodCount} methods (threshold: {GodClassMethods})"));
        }
    }

    static void DetectMethodSmells(TypeMetrics metrics, List<CodeSmell> smells)
    {
        foreach (var method in metrics.Methods)
        {
            if (method.LineCount >= LongMethodLines || method.CognitiveComplexity >= LongMethodCognitiveCC)
            {
                var severity = method.LineCount >= CriticalLongMethodLines || method.CognitiveComplexity >= CriticalCognitiveCC
                    ? SmellSeverity.Critical
                    : SmellSeverity.Warning;

                var parts = new List<string>();
                if (method.LineCount >= LongMethodLines)
                    parts.Add($"{method.LineCount} lines");
                if (method.CognitiveComplexity >= LongMethodCognitiveCC)
                    parts.Add($"cognitive CC {method.CognitiveComplexity}");

                smells.Add(new CodeSmell(
                    CodeSmellKind.LongMethod, severity, metrics.TypeName, method.MethodName,
                    string.Join(", ", parts)));
            }

            if (method.ParameterCount > ExcessiveParamCount)
            {
                smells.Add(new CodeSmell(
                    CodeSmellKind.ExcessiveParameters, SmellSeverity.Warning,
                    metrics.TypeName, method.MethodName,
                    $"{method.ParameterCount} parameters (threshold: {ExcessiveParamCount})"));
            }

            if (method.CyclomaticComplexity >= HighCyclomaticCC)
            {
                smells.Add(new CodeSmell(
                    CodeSmellKind.HighComplexity, SmellSeverity.Warning,
                    metrics.TypeName, method.MethodName,
                    $"cyclomatic CC {method.CyclomaticComplexity} (threshold: {HighCyclomaticCC})"));
            }

            if (method.CognitiveComplexity >= HighCognitiveCC && method.CognitiveComplexity < LongMethodCognitiveCC)
            {
                smells.Add(new CodeSmell(
                    CodeSmellKind.HighComplexity, SmellSeverity.Warning,
                    metrics.TypeName, method.MethodName,
                    $"cognitive CC {method.CognitiveComplexity} (threshold: {HighCognitiveCC})"));
            }

            if (method.MaxNestingDepth >= DeepNestingDepth)
            {
                var severity = method.MaxNestingDepth >= CriticalNestingDepth
                    ? SmellSeverity.Critical
                    : SmellSeverity.Warning;
                smells.Add(new CodeSmell(
                    CodeSmellKind.DeepNesting, severity, metrics.TypeName, method.MethodName,
                    $"nesting depth {method.MaxNestingDepth} (threshold: {DeepNestingDepth})"));
            }
        }
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
}
