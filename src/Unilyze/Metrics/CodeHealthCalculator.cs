using Unilyze.Detectors;
using Unilyze.Pipeline;
namespace Unilyze.Metrics;

internal sealed record MethodMetrics(
    string MethodName,
    int CognitiveComplexity,
    int CyclomaticComplexity,
    int MaxNestingDepth,
    int ParameterCount,
    int LineCount,
    int? StartLine = null,
    double? MaintainabilityIndex = null,
    double? HalsteadDifficulty = null,
    double? HalsteadEffort = null,
    double? HalsteadEstimatedBugs = null,
    string? MemberId = null);

internal sealed record TypeMetrics(
    string TypeName,
    string Namespace,
    string Assembly,
    int LineCount,
    int MethodCount,
    int MaxNestingDepth,
    double AverageCognitiveComplexity,
    int MaxCognitiveComplexity,
    double AverageCyclomaticComplexity,
    int MaxCyclomaticComplexity,
    int ExcessiveParameterMethodCount,
    double CodeHealth,
    IReadOnlyList<MethodMetrics> Methods,
    double? Lcom = null,
    int? Cbo = null,
    int? Dit = null,
    int? AfferentCoupling = null,
    int? EfferentCoupling = null,
    double? Instability = null,
    double? AverageMaintainabilityIndex = null,
    double? MinMaintainabilityIndex = null,
    int? Wmc = null,
    int? Noc = null,
    int? Rfc = null,
    double? TypeRank = null,
    int? BoxingCount = null,
    int? ClosureCaptureCount = null,
    int? ParamsAllocationCount = null,
    IReadOnlyList<CodeSmell>? CodeSmells = null,
    int? InformationalCount = null,
    string? FilePath = null,
    int? StartLine = null,
    string? QualifiedName = null,
    string? TypeId = null,
    double? CodeHealthV1 = null,
    string? CodeHealthCategory = null,
    int? HotPathMethodCount = null);

internal sealed record AssemblyHealthMetrics(
    double AverageCodeHealth,
    double MinCodeHealth,
    int HighComplexityTypeCount,
    int TotalMethods,
    double AverageCognitiveComplexity,
    double LocWeightedAverageCodeHealth,
    double WorstDecileCodeHealth);

internal static class CodeHealthCalculator
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
        var highComplexity = typeMetrics.Count(
            t => (t.CodeHealthCategory ?? Classify(t.CodeHealth)) == "alert");
        var locWeightedAverage = ComputeLocWeightedAverage(typeMetrics, t => t.CodeHealth);
        var worstDecileAverage = ComputeWorstDecileAverage(typeMetrics, t => t.CodeHealth);
        var avgCc = allMethods.Count > 0
            ? allMethods.Average(m => (double)m.CognitiveComplexity)
            : 0.0;

        return new AssemblyHealthMetrics(
            Math.Round(avgHealth, 1),
            Math.Round(minHealth, 1),
            highComplexity,
            allMethods.Count,
            Math.Round(avgCc, 1),
            Math.Round(locWeightedAverage, 1),
            Math.Round(worstDecileAverage, 1));
    }

    static readonly HashSet<string> ExecutableMemberKinds =
        ["Method", "Constructor", "Destructor", "Operator", "ConversionOperator"];

    static TypeMetrics ComputeSingleType(TypeNodeInfo type)
    {
        var methods = type.Members
            .Where(m => ExecutableMemberKinds.Contains(m.MemberKind) && m.CognitiveComplexity.HasValue)
            .Select(m =>
            {
                var mi = m.HalsteadVolume.HasValue
                    ? HalsteadCalculator.ComputeMaintainabilityIndex(
                        m.HalsteadVolume.Value, m.CyclomaticComplexity ?? 1, m.LineCount)
                    : (double?)null;
                return new MethodMetrics(
                    m.Name,
                    m.CognitiveComplexity!.Value,
                    m.CyclomaticComplexity ?? 1,
                    m.MaxNestingDepth ?? 0,
                    m.Parameters.Count,
                    m.LineCount,
                    m.StartLine,
                    mi.HasValue ? Math.Round(mi.Value, 1) : null,
                    m.HalsteadDifficulty.HasValue ? Math.Round(m.HalsteadDifficulty.Value, 2) : null,
                    m.HalsteadEffort.HasValue ? Math.Round(m.HalsteadEffort.Value, 1) : null,
                    m.HalsteadEstimatedBugs.HasValue ? Math.Round(m.HalsteadEstimatedBugs.Value, 4) : null,
                    m.MemberId);
            })
            .ToList();

        var methodCount = methods.Count;
        var avgCogCc = methodCount > 0 ? methods.Average(m => (double)m.CognitiveComplexity) : 0.0;
        var maxCogCc = methodCount > 0 ? methods.Max(m => m.CognitiveComplexity) : 0;
        var avgCycCc = methodCount > 0 ? methods.Average(m => (double)m.CyclomaticComplexity) : 0.0;
        var maxCycCc = methodCount > 0 ? methods.Max(m => m.CyclomaticComplexity) : 0;
        var excessiveParams = methods.Count(m => m.ParameterCount > 4);
        var maxNesting = methodCount > 0 ? methods.Max(m => m.MaxNestingDepth) : 0;

        var healthV1 = CalculateHealthScoreV1(
            avgCogCc, maxCogCc, type.LineCount, methodCount, maxNesting, excessiveParams);
        var health = CalculateHealthScore(
            avgCogCc, maxCogCc, type.LineCount, methodCount, maxNesting, excessiveParams);
        var roundedHealth = Math.Round(health, 1);

        var methodsWithMI = methods.Where(m => m.MaintainabilityIndex.HasValue).ToList();
        var avgMI = methodsWithMI.Count > 0
            ? Math.Round(methodsWithMI.Average(m => m.MaintainabilityIndex!.Value), 1)
            : (double?)null;
        var minMI = methodsWithMI.Count > 0
            ? Math.Round(methodsWithMI.Min(m => m.MaintainabilityIndex!.Value), 1)
            : (double?)null;

        return new TypeMetrics(
            type.Name,
            type.Namespace,
            type.Assembly,
            type.LineCount,
            methodCount,
            maxNesting,
            Math.Round(avgCogCc, 1),
            maxCogCc,
            Math.Round(avgCycCc, 1),
            maxCycCc,
            excessiveParams,
            roundedHealth,
            methods,
            AverageMaintainabilityIndex: avgMI,
            MinMaintainabilityIndex: minMI,
            FilePath: type.FilePath,
            StartLine: type.StartLine,
            QualifiedName: type.QualifiedName,
            TypeId: type.TypeId,
            CodeHealthV1: Math.Round(healthV1, 1),
            CodeHealthCategory: Classify(roundedHealth));
    }

    internal static double CalculateHealthScore(
        double avgCc, int maxCc, int lineCount,
        int methodCount, int maxNesting, int excessiveParams)
    {
        _ = avgCc;

        // Complexity cutpoints are the residual P70/P80/P90 values projected at
        // the corpus reference LOC. Runtime scoring stays monotone in every input.
        var cognitivePenalty = PiecewisePenalty(maxCc, 16.0, 19.0, 24.0, 4.0);
        var nestingPenalty = PiecewisePenalty(maxNesting, 3.0, 4.0, 5.0, 4.0);
        var complexityPenalty = Math.Max(cognitivePenalty, nestingPenalty);

        var linePenalty = PiecewisePenalty(lineCount, 272.0, 391.0, 878.0, 3.0);
        var methodPenalty = PiecewisePenalty(methodCount, 11.0, 16.0, 34.0, 3.0);
        var sizePenalty = Math.Max(linePenalty, methodPenalty);

        var interfacePenalty = PiecewisePenalty(excessiveParams, 0.0, 1.0, 2.0, 2.0);
        return Math.Clamp(10.0 - complexityPenalty - sizePenalty - interfacePenalty, 1.0, 10.0);
    }

    internal static double CalculateHealthScoreV1(
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

    internal static double PiecewisePenalty(
        double value,
        double safeUpper,
        double warningUpper,
        double alertUpper,
        double maximumPenalty)
    {
        if (value <= safeUpper)
            return 0.0;
        if (value >= alertUpper)
            return maximumPenalty;

        if (value <= warningUpper)
        {
            var denominator = warningUpper - safeUpper;
            if (denominator <= 0)
                return maximumPenalty * 0.25;

            var ratio = (value - safeUpper) / denominator;
            return ratio * maximumPenalty * 0.25;
        }

        var alertDenominator = alertUpper - warningUpper;
        if (alertDenominator <= 0)
            return maximumPenalty;

        var alertRatio = (value - warningUpper) / alertDenominator;
        return maximumPenalty * (0.25 + alertRatio * 0.75);
    }

    public static string Classify(double codeHealth) => codeHealth switch
    {
        >= 9.0 => "healthy",
        >= 4.0 => "warning",
        _ => "alert",
    };

    internal static double ComputeLocWeightedAverage(
        IReadOnlyList<TypeMetrics> typeMetrics,
        Func<TypeMetrics, double> selector)
    {
        var positiveLoc = typeMetrics.Where(t => t.LineCount > 0).ToList();
        if (positiveLoc.Count == 0)
            return typeMetrics.Count == 0 ? 0.0 : typeMetrics.Average(selector);

        var totalLoc = positiveLoc.Sum(t => (long)t.LineCount);
        return positiveLoc.Sum(t => selector(t) * t.LineCount) / totalLoc;
    }

    internal static double ComputeWorstDecileAverage(
        IReadOnlyList<TypeMetrics> typeMetrics,
        Func<TypeMetrics, double> selector)
    {
        if (typeMetrics.Count == 0)
            return 0.0;

        var tailCount = Math.Max(1, (int)Math.Ceiling(typeMetrics.Count * 0.1));
        return typeMetrics.OrderBy(selector).Take(tailCount).Average(selector);
    }

    // Linear interpolation: value <= low10 -> 10, value >= high1 -> 1
    // Between low10..low5 -> 10..5, between low5..high1 -> 5..1
    internal static double Interpolate(double value, double low10, double low5, double high5, double high1)
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

}
