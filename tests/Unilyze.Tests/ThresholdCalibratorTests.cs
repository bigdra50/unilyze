using Unilyze;

namespace Unilyze.Tests;

public sealed class ThresholdCalibratorTests
{
    static AnalysisResult MakeSnapshot(
        string name,
        IReadOnlyList<(int TypeLoc, int MethodCount, IReadOnlyList<(int Loc, int Cyc, int Cog, int Nest, int Params)> Methods)> types,
        int metricsVersion = AnalysisResult.CurrentMetricsVersion)
    {
        var typeMetrics = types.Select((t, i) =>
        {
            var methods = t.Methods;
            var maxNest = methods.Count > 0 ? methods.Max(m => m.Nest) : 0;
            var avgCog = methods.Count > 0 ? methods.Average(m => (double)m.Cog) : 0;
            var maxCog = methods.Count > 0 ? methods.Max(m => m.Cog) : 0;
            var avgCyc = methods.Count > 0 ? methods.Average(m => (double)m.Cyc) : 0;
            var maxCyc = methods.Count > 0 ? methods.Max(m => m.Cyc) : 0;
            return new TypeMetrics(
                $"Type{i}",
                "Ns",
                "Asm",
                t.TypeLoc,
                t.MethodCount,
                maxNest,
                avgCog,
                maxCog,
                avgCyc,
                maxCyc,
                methods.Count(m => m.Params > 5),
                8.0,
                methods.Select((m, j) => new MethodMetrics(
                    $"M{j}",
                    m.Cog,
                    m.Cyc,
                    m.Nest,
                    m.Params,
                    m.Loc)).ToList());
        }).ToList();

        return new AnalysisResult(
            name,
            DateTimeOffset.UtcNow,
            [],
            [],
            [],
            typeMetrics,
            MetricsVersion: metricsVersion,
            ToolVersion: "test");
    }

    [Fact]
    public void WeightedPercentile_SingleValue_ReturnsValue()
    {
        var samples = new List<(double Value, double Weight)> { (42, 1.0) };
        Assert.Equal(42, ThresholdCalibrator.WeightedPercentile(samples, 70));
    }

    [Fact]
    public void WeightedPercentile_Ties_UsesCumulativeWeight()
    {
        var samples = new List<(double Value, double Weight)>
        {
            (10, 0.25),
            (10, 0.25),
            (20, 0.25),
            (30, 0.25),
        };

        Assert.Equal(10, ThresholdCalibrator.WeightedPercentile(samples, 25));
        Assert.Equal(10, ThresholdCalibrator.WeightedPercentile(samples, 50));
        Assert.Equal(20, ThresholdCalibrator.WeightedPercentile(samples, 75));
    }

    [Fact]
    public void Calibrate_TwoSystems_PoolsWithEqualWeight()
    {
        var a = MakeSnapshot("system-a", [
            (100, 2, [(10, 2, 3, 1, 1), (30, 6, 8, 2, 2)]),
        ]);
        var b = MakeSnapshot("system-b", [
            (200, 2, [(20, 4, 5, 1, 2), (80, 12, 14, 3, 3)]),
        ]);

        var result = ThresholdCalibrator.Calibrate([
            ("a.json", a),
            ("b.json", b),
        ]);

        Assert.Equal(2, result.Sources.Count);
        Assert.Equal(AnalysisResult.CurrentMetricsVersion, result.MetricsVersion);
        Assert.Equal("moderate", result.RiskCategories.Moderate);
        Assert.True(result.Metrics.MethodLines.RiskBands.ModerateUpper > 0);
        Assert.True(result.Metrics.CyclomaticComplexity.RiskBands.HighUpper
            >= result.Metrics.CyclomaticComplexity.RiskBands.ModerateUpper);
        Assert.Contains("LongMethod", result.UnilyzeConfigFragment.Smells.Keys);
    }

    [Fact]
    public void Calibrate_SingleSnapshot_Throws()
    {
        var snapshot = MakeSnapshot("only", [(50, 1, [(10, 1, 1, 0, 1)])]);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ThresholdCalibrator.Calibrate([("only.json", snapshot)]));
        Assert.Contains("At least two", ex.Message);
    }

    [Fact]
    public void Calibrate_MetricsVersionMismatch_Throws()
    {
        var current = MakeSnapshot("current", [(50, 1, [(10, 1, 1, 0, 1)])]);
        var older = MakeSnapshot("older", [(50, 1, [(10, 1, 1, 0, 1)])], metricsVersion: 2);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ThresholdCalibrator.Calibrate([
                ("current.json", current),
                ("older.json", older),
            ]));
        Assert.Contains("metricsVersion mismatch", ex.Message);
    }

    [Fact]
    public void Calibrate_EmptyMethodTypes_SkipsGracefully()
    {
        var withMethods = MakeSnapshot("with", [(40, 1, [(15, 3, 4, 1, 1)])]);
        var emptyMethods = MakeSnapshot("empty", [(40, 0, [])]);

        var result = ThresholdCalibrator.Calibrate([
            ("with.json", withMethods),
            ("empty.json", emptyMethods),
        ]);

        Assert.Equal(2, result.Sources.Count);
        Assert.Equal(0, result.Sources[1].MethodCount);
    }

    [Fact]
    public void Calibrate_ParameterCount_Uses809095Percentiles()
    {
        var a = MakeSnapshot("a", [(100, 3, [(10, 1, 1, 0, 1), (10, 1, 1, 0, 2), (10, 1, 1, 0, 3)])]);
        var b = MakeSnapshot("b", [(100, 3, [(10, 1, 1, 0, 2), (10, 1, 1, 0, 3), (10, 1, 1, 0, 4)])]);

        var result = ThresholdCalibrator.Calibrate([
            ("a.json", a),
            ("b.json", b),
        ]);

        Assert.Equal([80, 90, 95], result.Metrics.ParameterCount.PercentileLevels);
    }
}
