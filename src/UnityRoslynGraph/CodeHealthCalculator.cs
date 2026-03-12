namespace UnityRoslynGraph;

public sealed record MethodMetrics(
    string MethodName,
    int CognitiveComplexity,
    int ParameterCount,
    int LineCount);

public sealed record TypeMetrics(
    string TypeName,
    string Assembly,
    int LineCount,
    int MethodCount,
    int MaxNestingDepth,
    double AverageCognitiveComplexity,
    int MaxCognitiveComplexity,
    int ExcessiveParameterMethodCount,
    double CodeHealth,
    IReadOnlyList<MethodMetrics> Methods);

public sealed record AssemblyHealthMetrics(
    double AverageCodeHealth,
    double MinCodeHealth,
    int HighComplexityTypeCount,
    int TotalMethods,
    double AverageCognitiveComplexity);

public static class CodeHealthCalculator
{
    public static IReadOnlyList<TypeMetrics> ComputeTypeMetrics(IReadOnlyList<TypeNodeInfo> allTypes)
    {
        return allTypes
            .Where(t => t.Kind is not ("enum" or "delegate"))
            .Select(ComputeSingleType)
            .ToList();
    }

    public static AssemblyHealthMetrics? ComputeAssemblyHealth(IReadOnlyList<TypeMetrics> typeMetrics)
    {
        if (typeMetrics.Count == 0) return null;

        var allMethods = typeMetrics.SelectMany(t => t.Methods).ToList();
        var avgHealth = typeMetrics.Average(t => t.CodeHealth);
        var minHealth = typeMetrics.Min(t => t.CodeHealth);
        var highComplexity = typeMetrics.Count(t => t.CodeHealth < 4.0);
        var avgCc = allMethods.Count > 0
            ? allMethods.Average(m => (double)m.CognitiveComplexity)
            : 0.0;

        return new AssemblyHealthMetrics(
            Math.Round(avgHealth, 1),
            Math.Round(minHealth, 1),
            highComplexity,
            allMethods.Count,
            Math.Round(avgCc, 1));
    }

    static TypeMetrics ComputeSingleType(TypeNodeInfo type)
    {
        var methods = type.Members
            .Where(m => m.MemberKind == "Method" && m.CognitiveComplexity.HasValue)
            .Select(m => new MethodMetrics(
                m.Name,
                m.CognitiveComplexity!.Value,
                m.Parameters.Count,
                0))
            .ToList();

        var methodCount = methods.Count;
        var avgCc = methodCount > 0 ? methods.Average(m => (double)m.CognitiveComplexity) : 0.0;
        var maxCc = methodCount > 0 ? methods.Max(m => m.CognitiveComplexity) : 0;
        var excessiveParams = methods.Count(m => m.ParameterCount > 4);
        var maxNesting = EstimateMaxNestingDepth(type);

        var health = CalculateHealthScore(avgCc, maxCc, type.LineCount, methodCount, maxNesting, excessiveParams);

        return new TypeMetrics(
            type.Name,
            type.Assembly,
            type.LineCount,
            methodCount,
            maxNesting,
            Math.Round(avgCc, 1),
            maxCc,
            excessiveParams,
            Math.Round(health, 1),
            methods);
    }

    static double CalculateHealthScore(
        double avgCc, int maxCc, int lineCount,
        int methodCount, int maxNesting, int excessiveParams)
    {
        var avgCcScore = Interpolate(avgCc, 5, 10, 15, 25);
        var maxCcScore = Interpolate(maxCc, 10, 15, 25, 40);
        var lineScore = Interpolate(lineCount, 200, 300, 500, 800);
        var methodScore = Interpolate(methodCount, 10, 15, 25, 40);
        var nestScore = Interpolate(maxNesting, 3, 4, 5, 7);
        var paramScore = Interpolate(excessiveParams, 0, 1, 2, 4);

        return avgCcScore * 0.25
             + maxCcScore * 0.20
             + lineScore * 0.15
             + methodScore * 0.10
             + nestScore * 0.15
             + paramScore * 0.15;
    }

    // Linear interpolation: value <= low10 -> 10, value >= high1 -> 1
    // Between low10..low5 -> 10..5, between low5..high1 -> 5..1
    static double Interpolate(double value, double low10, double low5, double high5, double high1)
    {
        if (value <= low10) return 10.0;
        if (value >= high1) return 1.0;
        if (value <= low5)
        {
            var ratio = (value - low10) / (low5 - low10);
            return 10.0 - ratio * 5.0;
        }
        else
        {
            var ratio = (value - low5) / (high1 - low5);
            return 5.0 - ratio * 4.0;
        }
    }

    static int EstimateMaxNestingDepth(TypeNodeInfo type)
    {
        // Use CC as a proxy: high CC implies deep nesting
        // A rough heuristic: max nesting ~ sqrt(maxCC)
        var maxCc = type.Members
            .Where(m => m.CognitiveComplexity.HasValue)
            .Select(m => m.CognitiveComplexity!.Value)
            .DefaultIfEmpty(0)
            .Max();

        return maxCc switch
        {
            0 => 0,
            <= 3 => 1,
            <= 8 => 2,
            <= 15 => 3,
            <= 25 => 4,
            <= 40 => 5,
            <= 60 => 6,
            _ => 7
        };
    }
}
