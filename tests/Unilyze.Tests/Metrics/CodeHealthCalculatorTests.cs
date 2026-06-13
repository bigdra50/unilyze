namespace Unilyze.Tests.Metrics;

public sealed class CodeHealthCalculatorTests
{
    [Fact]
    public void CalculateHealthScore_AllFactorsSafe_Returns10()
    {
        var actual = CodeHealthCalculator.CalculateHealthScore(
            avgCc: 0,
            maxCc: 0,
            lineCount: 0,
            methodCount: 0,
            maxNesting: 0,
            excessiveParams: 0);

        Assert.Equal(10.0, actual);
    }

    [Fact]
    public void CalculateHealthScore_AllFactorsSaturated_Returns1()
    {
        var actual = CodeHealthCalculator.CalculateHealthScore(
            avgCc: 100,
            maxCc: 100,
            lineCount: 10000,
            methodCount: 100,
            maxNesting: 20,
            excessiveParams: 20);

        Assert.Equal(1.0, actual);
    }

    [Theory]
    [InlineData("avgCogCc")]
    [InlineData("maxCogCc")]
    [InlineData("lineCount")]
    [InlineData("methodCount")]
    [InlineData("maxNesting")]
    [InlineData("excessiveParams")]
    public void CalculateHealthScore_IncreasingAnyInput_DoesNotImproveScore(string factor)
    {
        var previous = 10.0;
        for (var value = 0; value <= 1000; value++)
        {
            var actual = CalculateWithFactor(factor, value);

            Assert.True(
                actual <= previous,
                $"Monotonicity violated for {factor} at {value}: {actual} > {previous}");
            previous = actual;
        }
    }

    [Theory]
    [InlineData(24, 0, 0, 0, 6.0)]
    [InlineData(0, 878, 0, 0, 7.0)]
    [InlineData(0, 0, 5, 0, 6.0)]
    [InlineData(0, 0, 0, 2, 8.0)]
    public void CalculateHealthScore_OneSaturatedFactor_IsBelowHealthyFloor(
        int maxCogCc,
        int lineCount,
        int maxNesting,
        int excessiveParams,
        double expected)
    {
        var actual = CodeHealthCalculator.CalculateHealthScore(
            avgCc: 0,
            maxCc: maxCogCc,
            lineCount: lineCount,
            methodCount: 0,
            maxNesting: maxNesting,
            excessiveParams: excessiveParams);

        Assert.Equal(expected, actual);
        Assert.True(actual < 9.0);
    }

    [Theory]
    [InlineData(9.0, "healthy")]
    [InlineData(10.0, "healthy")]
    [InlineData(4.0, "warning")]
    [InlineData(8.9, "warning")]
    [InlineData(3.9, "alert")]
    [InlineData(1.0, "alert")]
    public void Classify_UsesDocumentedBoundaries(double score, string expected)
    {
        var actual = CodeHealthCalculator.Classify(score);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ComputeAssemblyHealth_ComputesWeightedAndWorstDecileAggregates()
    {
        var metrics = Enumerable.Range(1, 10)
            .Select(index => CreateTypeMetrics(
                lineCount: index == 1 ? 100 : 10,
                codeHealth: index))
            .ToList();

        var actual = CodeHealthCalculator.ComputeAssemblyHealth(metrics);

        Assert.NotNull(actual);
        Assert.Equal(3.4, actual.LocWeightedAverageCodeHealth);
        Assert.Equal(1.0, actual.WorstDecileCodeHealth);
        Assert.Equal(3, actual.HighComplexityTypeCount);
    }

    [Fact]
    public void CalculateHealthScoreV1_PreservesLegacyWeightedFormula()
    {
        var actual = CodeHealthCalculator.CalculateHealthScoreV1(
            avgCc: 0,
            maxCc: 0,
            lineCount: 0,
            methodCount: 0,
            maxNesting: 0,
            excessiveParams: 0);

        Assert.Equal(10.0, actual);
    }

    [Fact]
    public void CodeHealthVersionSelector_Select_UsesLegacyScoreWithoutChangingSource()
    {
        var type = CreateTypeMetrics(lineCount: 10, codeHealth: 5.0) with { CodeHealthV1 = 8.0 };
        var source = new AnalysisResult(
            "/test",
            DateTimeOffset.UtcNow,
            [],
            [],
            [],
            [type]);

        var actual = CodeHealthVersionSelector.Select(source, useV1: true);

        Assert.Equal(8.0, actual.TypeMetrics![0].CodeHealth);
        Assert.Equal(5.0, source.TypeMetrics![0].CodeHealth);
    }

    static double CalculateWithFactor(string factor, int value)
        => CodeHealthCalculator.CalculateHealthScore(
            avgCc: factor == "avgCogCc" ? value : 0,
            maxCc: factor == "maxCogCc" ? value : 0,
            lineCount: factor == "lineCount" ? value : 0,
            methodCount: factor == "methodCount" ? value : 0,
            maxNesting: factor == "maxNesting" ? value : 0,
            excessiveParams: factor == "excessiveParams" ? value : 0);

    static TypeMetrics CreateTypeMetrics(int lineCount, double codeHealth)
        => new(
            "Type",
            "Namespace",
            "Assembly",
            lineCount,
            1,
            0,
            0,
            0,
            0,
            0,
            0,
            codeHealth,
            [],
            CodeHealthCategory: CodeHealthCalculator.Classify(codeHealth));
}
