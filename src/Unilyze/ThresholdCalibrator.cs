namespace Unilyze;

/// <summary>
/// Derives smell-threshold candidates from pooled analysis snapshots using the
/// Alves, Ypma &amp; Visser (ICSM 2010) weighted-percentile procedure.
/// </summary>
public static class ThresholdCalibrator
{
    public const string Methodology =
        "Alves, Ypma & Visser (ICSM 2010): LOC-weighted method metrics pooled with equal per-system weight; "
        + "thresholds at 70/80/90 percentiles (80/90/95 for parameter count).";

    static readonly int[] DefaultPercentileLevels = [70, 80, 90];
    static readonly int[] ParameterPercentileLevels = [80, 90, 95];

    public static CalibrateResult Calibrate(
        IReadOnlyList<(string FileName, AnalysisResult Result)> snapshots)
    {
        if (snapshots.Count < 2)
            throw new InvalidOperationException("At least two analysis snapshots are required.");

        var metricsVersions = snapshots.Select(s => s.Result.MetricsVersion).Distinct().ToList();
        if (metricsVersions.Count > 1)
        {
            var formatted = string.Join(", ", metricsVersions.Select(ToolVersionInfo.FormatMetricsVersion));
            throw new InvalidOperationException(
                $"metricsVersion mismatch across snapshots ({formatted}). "
                + "Re-analyze all systems with the same unilyze version before calibrating.");
        }

        var systemCount = snapshots.Count;
        var methodLines = CollectMethodWeighted(snapshots, systemCount, m => m.LineCount);
        var cycCc = CollectMethodWeighted(snapshots, systemCount, m => m.CyclomaticComplexity);
        var cogCc = CollectMethodWeighted(snapshots, systemCount, m => m.CognitiveComplexity);
        var nesting = CollectMethodWeighted(snapshots, systemCount, m => m.MaxNestingDepth);
        var parameters = CollectMethodWeighted(snapshots, systemCount, m => m.ParameterCount);
        var methodsPerType = CollectTypeWeighted(snapshots, systemCount, t => t.MethodCount);
        var typeLines = CollectTypeWeighted(snapshots, systemCount, t => t.LineCount);

        var metricsVersion = metricsVersions[0];
        if (metricsVersion == 0)
            metricsVersion = AnalysisResult.CurrentMetricsVersion;

        var sources = snapshots.Select(s => BuildSourceInfo(s.FileName, s.Result)).ToList();
        var metrics = new CalibrateMetricsBlock(
            BuildMetricThresholds("method", methodLines, DefaultPercentileLevels),
            BuildMetricThresholds("method", cycCc, DefaultPercentileLevels),
            BuildMetricThresholds("method", cogCc, DefaultPercentileLevels),
            BuildMetricThresholds("method", nesting, DefaultPercentileLevels),
            BuildParameterThresholds(parameters),
            BuildMetricThresholds("type", methodsPerType, DefaultPercentileLevels),
            BuildMetricThresholds("type", typeLines, DefaultPercentileLevels));

        return new CalibrateResult(
            Methodology,
            metricsVersion,
            ToolVersionInfo.Current,
            sources,
            new CalibrateRiskCategories("low", "moderate", "high", "veryHigh"),
            metrics,
            BuildConfigFragment(metrics));
    }

    static CalibrateSourceInfo BuildSourceInfo(string fileName, AnalysisResult result)
    {
        var typeMetrics = result.TypeMetrics ?? [];
        var methodCount = typeMetrics.Sum(t => t.Methods.Count);
        var totalMethodLoc = typeMetrics.Sum(t => t.Methods.Sum(m => m.LineCount));
        return new CalibrateSourceInfo(
            fileName,
            result.ProjectPath,
            methodCount,
            typeMetrics.Count,
            totalMethodLoc);
    }

    static List<(double Value, double Weight)> CollectMethodWeighted(
        IReadOnlyList<(string FileName, AnalysisResult Result)> snapshots,
        int systemCount,
        Func<MethodMetrics, int> selector)
    {
        var pooled = new List<(double Value, double Weight)>();
        foreach (var (_, result) in snapshots)
        {
            var typeMetrics = result.TypeMetrics ?? [];
            var methods = typeMetrics.SelectMany(t => t.Methods).Where(m => m.LineCount > 0).ToList();
            if (methods.Count == 0)
                continue;

            var totalLoc = methods.Sum(m => m.LineCount);
            if (totalLoc <= 0)
                continue;

            foreach (var method in methods)
            {
                var share = (double)method.LineCount / totalLoc / systemCount;
                pooled.Add((selector(method), share));
            }
        }

        return pooled;
    }

    static List<(double Value, double Weight)> CollectTypeWeighted(
        IReadOnlyList<(string FileName, AnalysisResult Result)> snapshots,
        int systemCount,
        Func<TypeMetrics, int> selector)
    {
        var pooled = new List<(double Value, double Weight)>();
        foreach (var (_, result) in snapshots)
        {
            var typeMetrics = result.TypeMetrics ?? [];
            var types = typeMetrics.Where(t => t.LineCount > 0).ToList();
            if (types.Count == 0)
                continue;

            var totalLoc = types.Sum(t => t.LineCount);
            if (totalLoc <= 0)
                continue;

            foreach (var type in types)
            {
                var share = (double)type.LineCount / totalLoc / systemCount;
                pooled.Add((selector(type), share));
            }
        }

        return pooled;
    }

    static CalibrateMetricThresholds BuildMetricThresholds(
        string unit,
        IReadOnlyList<(double Value, double Weight)> samples,
        IReadOnlyList<int> levels)
    {
        var percentiles = levels.Select(level => WeightedPercentile(samples, level)).ToList();
        return new CalibrateMetricThresholds(
            unit,
            percentiles,
            levels,
            BuildRiskBands(percentiles));
    }

    static CalibrateParameterThresholds BuildParameterThresholds(
        IReadOnlyList<(double Value, double Weight)> samples)
    {
        var percentiles = ParameterPercentileLevels
            .Select(level => WeightedPercentile(samples, level))
            .ToList();
        return new CalibrateParameterThresholds(
            "method",
            percentiles,
            ParameterPercentileLevels,
            BuildRiskBands(percentiles));
    }

    static CalibrateRiskBands BuildRiskBands(IReadOnlyList<double> percentiles)
    {
        if (percentiles.Count < 3)
            return new CalibrateRiskBands(0, 0, 0);

        return new CalibrateRiskBands(
            Ceiling(percentiles[0]),
            Ceiling(percentiles[1]),
            Ceiling(percentiles[2]));
    }

    static CalibrateUnilyzeConfigFragment BuildConfigFragment(CalibrateMetricsBlock metrics)
        => new(new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.OrdinalIgnoreCase)
        {
            ["LongMethod"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["lines"] = ToInt(metrics.MethodLines.RiskBands.ModerateUpper),
                ["cogCc"] = ToInt(metrics.CognitiveComplexity.RiskBands.ModerateUpper),
                ["criticalLines"] = ToInt(metrics.MethodLines.RiskBands.HighUpper),
                ["criticalCogCc"] = ToInt(metrics.CognitiveComplexity.RiskBands.HighUpper),
            },
            ["HighComplexity"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["cycCc"] = ToInt(metrics.CyclomaticComplexity.RiskBands.ModerateUpper),
                ["cogCc"] = ToInt(metrics.CognitiveComplexity.RiskBands.ModerateUpper),
            },
            ["DeepNesting"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["depth"] = ToInt(metrics.MaxNestingDepth.RiskBands.ModerateUpper),
                ["criticalDepth"] = ToInt(metrics.MaxNestingDepth.RiskBands.HighUpper),
            },
            ["ExcessiveParameters"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["max"] = ToInt(metrics.ParameterCount.RiskBands.ModerateUpper),
            },
            ["GodClass"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["lines"] = ToInt(metrics.TypeLines.RiskBands.ModerateUpper),
                ["methods"] = ToInt(metrics.MethodsPerType.RiskBands.ModerateUpper),
                ["criticalLines"] = ToInt(metrics.TypeLines.RiskBands.HighUpper),
            },
        });

    public static double WeightedPercentile(
        IReadOnlyList<(double Value, double Weight)> samples,
        double percentile)
    {
        if (samples.Count == 0)
            return 0;

        var totalWeight = samples.Sum(s => s.Weight);
        if (totalWeight <= 0)
            return samples.Max(s => s.Value);

        var target = totalWeight * percentile / 100.0;
        var sorted = samples.OrderBy(s => s.Value).ToList();
        var cumulative = 0.0;
        foreach (var sample in sorted)
        {
            cumulative += sample.Weight;
            if (cumulative >= target)
                return sample.Value;
        }

        return sorted[^1].Value;
    }

    static int Ceiling(double value) => (int)Math.Ceiling(value);
    static int ToInt(double value) => (int)Math.Round(value);
}
