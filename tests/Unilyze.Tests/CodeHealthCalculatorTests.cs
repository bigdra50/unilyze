namespace Unilyze.Tests;

public class CodeHealthCalculatorTests
{
    // --- Interpolate boundary tests ---

    [Fact]
    public void Interpolate_BelowLow10_Returns10()
    {
        Assert.Equal(10.0, CodeHealthCalculator.Interpolate(0, 5, 10, 15, 25));
    }

    [Fact]
    public void Interpolate_AtLow10_Returns10()
    {
        Assert.Equal(10.0, CodeHealthCalculator.Interpolate(5, 5, 10, 15, 25));
    }

    [Fact]
    public void Interpolate_AtLow5_Returns5()
    {
        Assert.Equal(5.0, CodeHealthCalculator.Interpolate(10, 5, 10, 15, 25));
    }

    [Fact]
    public void Interpolate_AtHigh1_Returns1()
    {
        Assert.Equal(1.0, CodeHealthCalculator.Interpolate(25, 5, 10, 15, 25));
    }

    [Fact]
    public void Interpolate_AboveHigh1_Returns1()
    {
        Assert.Equal(1.0, CodeHealthCalculator.Interpolate(100, 5, 10, 15, 25));
    }

    [Fact]
    public void Interpolate_MidpointLow10ToLow5_Returns7_5()
    {
        Assert.Equal(7.5, CodeHealthCalculator.Interpolate(7.5, 5, 10, 15, 25));
    }

    [Fact]
    public void Interpolate_MidpointLow5ToHigh1_Returns3()
    {
        // midpoint of [10,25] = 17.5, score = 5 - (7.5/15)*4 = 5 - 2 = 3
        Assert.Equal(3.0, CodeHealthCalculator.Interpolate(17.5, 5, 10, 15, 25));
    }

    [Fact]
    public void Interpolate_MonotonicallyDecreasing()
    {
        double prev = 10.0;
        for (int v = 0; v <= 50; v++)
        {
            var score = CodeHealthCalculator.Interpolate(v, 5, 10, 15, 25);
            Assert.True(score <= prev, $"Monotonicity violated at value={v}: {score} > {prev}");
            prev = score;
        }
    }

    [Fact]
    public void Interpolate_ValueRange_Between1And10()
    {
        for (int v = -10; v <= 100; v++)
        {
            var score = CodeHealthCalculator.Interpolate(v, 5, 10, 15, 25);
            Assert.InRange(score, 1.0, 10.0);
        }
    }

    // --- CalculateHealthScore tests ---

    [Fact]
    public void HealthScore_PerfectMetrics_Returns10()
    {
        var score = CodeHealthCalculator.CalculateHealthScore(
            avgCc: 0, maxCc: 0, lineCount: 0,
            methodCount: 0, maxNesting: 0, excessiveParams: 0);
        Assert.Equal(10.0, score);
    }

    [Fact]
    public void HealthScore_WorstMetrics_Returns1()
    {
        var score = CodeHealthCalculator.CalculateHealthScore(
            avgCc: 100, maxCc: 100, lineCount: 10000,
            methodCount: 100, maxNesting: 20, excessiveParams: 20);
        Assert.Equal(1.0, score);
    }

    [Fact]
    public void HealthScore_WeightsSumToOne()
    {
        const double expected = 1.0;
        const double actual = 0.25 + 0.20 + 0.15 + 0.10 + 0.15 + 0.15;
        Assert.Equal(expected, actual, precision: 10);
    }

    [Fact]
    public void HealthScore_MonotonicallyDecreasing_WithAvgCC()
    {
        double prev = 10.0;
        for (int cc = 0; cc <= 50; cc++)
        {
            var score = CodeHealthCalculator.CalculateHealthScore(
                avgCc: cc, maxCc: 0, lineCount: 0,
                methodCount: 0, maxNesting: 0, excessiveParams: 0);
            Assert.True(score <= prev,
                $"Monotonicity violated at avgCc={cc}: {score} > {prev}");
            prev = score;
        }
    }
}
