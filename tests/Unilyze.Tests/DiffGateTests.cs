namespace Unilyze.Tests;

public sealed class DiffGateTests
{
    static StatuslineFormatter.Summary Summary(
        double avgHealth = 9.0,
        double minHealth = 9.0,
        int warnings = 0,
        int criticals = 0) =>
        new(avgHealth, minHealth, warnings, criticals, 10, 80.0, 0, 0);

    [Fact]
    public void EvaluateRegression_NoChange_NoRegression()
    {
        var s = Summary();
        var result = DiffGate.EvaluateRegression(s, s);
        Assert.False(result.HasRegression);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void EvaluateRegression_AllImproved_NoRegression()
    {
        var before = Summary(avgHealth: 7.0, minHealth: 5.0, warnings: 4, criticals: 1);
        var after = Summary(avgHealth: 8.0, minHealth: 6.0, warnings: 2, criticals: 0);
        Assert.False(DiffGate.EvaluateRegression(before, after).HasRegression);
    }

    [Fact]
    public void EvaluateRegression_MinCodeHealthDropped_Regresses()
    {
        var before = Summary(minHealth: 7.2);
        var after = Summary(minHealth: 6.8);
        var result = DiffGate.EvaluateRegression(before, after);
        Assert.True(result.HasRegression);
        Assert.Contains("min CodeHealth 7.2 -> 6.8", result.Reason);
    }

    [Fact]
    public void EvaluateRegression_AvgCodeHealthDropped_Regresses()
    {
        // Min unchanged, only the average drops.
        var before = Summary(avgHealth: 8.5, minHealth: 5.0);
        var after = Summary(avgHealth: 8.0, minHealth: 5.0);
        var result = DiffGate.EvaluateRegression(before, after);
        Assert.True(result.HasRegression);
        Assert.Contains("avg CodeHealth 8.5 -> 8", result.Reason);
    }

    [Fact]
    public void EvaluateRegression_WarningSmellsIncreased_Regresses()
    {
        var before = Summary(warnings: 3);
        var after = Summary(warnings: 5);
        var result = DiffGate.EvaluateRegression(before, after);
        Assert.True(result.HasRegression);
        Assert.Contains("warning smells 3 -> 5", result.Reason);
    }

    [Fact]
    public void EvaluateRegression_CriticalSmellsIncreased_Regresses()
    {
        var before = Summary(criticals: 0);
        var after = Summary(criticals: 2);
        var result = DiffGate.EvaluateRegression(before, after);
        Assert.True(result.HasRegression);
        Assert.Contains("critical smells 0 -> 2", result.Reason);
    }

    [Fact]
    public void EvaluateRegression_SmellsDecreased_NoRegression()
    {
        var before = Summary(warnings: 5, criticals: 2);
        var after = Summary(warnings: 1, criticals: 0);
        Assert.False(DiffGate.EvaluateRegression(before, after).HasRegression);
    }

    [Fact]
    public void EvaluateRegression_TinyHealthFloatNoise_NoRegression()
    {
        // Below the epsilon threshold; must not count as a regression.
        var before = Summary(minHealth: 7.0, avgHealth: 8.0);
        var after = Summary(minHealth: 7.0 - 0.00001, avgHealth: 8.0 - 0.00001);
        Assert.False(DiffGate.EvaluateRegression(before, after).HasRegression);
    }

    [Fact]
    public void EvaluateRegression_MinCheckedBeforeAvg()
    {
        // Both regress; min is reported first.
        var before = Summary(avgHealth: 8.5, minHealth: 7.0);
        var after = Summary(avgHealth: 8.0, minHealth: 6.0);
        var result = DiffGate.EvaluateRegression(before, after);
        Assert.True(result.HasRegression);
        Assert.Contains("min CodeHealth", result.Reason);
    }
}
