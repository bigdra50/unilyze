namespace Unilyze.Tests;

public sealed class BadgeGateTests
{
    static StatuslineFormatter.Summary Summary(
        double avgHealth = 9.0,
        double minHealth = 9.0,
        int warnings = 0,
        int criticals = 0,
        int typeCount = 10,
        double avgMi = 80.0,
        int miBearingCount = 10,
        int hotPathSmells = 0,
        int hotPathMethods = 0) =>
        new(
            avgHealth,
            minHealth,
            warnings,
            criticals,
            typeCount,
            avgMi,
            0,
            0,
            miBearingCount,
            HotPathSmellCount: hotPathSmells,
            HotPathMethodCount: hotPathMethods);

    [Fact]
    public void Evaluate_NoGateFlags_Passes()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.CodeHealth, Summary(minHealth: 1.0), null, null);
        Assert.Equal(GateOutcome.Pass, result.Outcome);
    }

    // --- codehealth (gates on min) ---

    [Fact]
    public void Evaluate_CodeHealth_MinBelowThreshold_Fails()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.CodeHealth, Summary(minHealth: 6.8), "7.2", null);
        Assert.Equal(GateOutcome.Fail, result.Outcome);
        Assert.Contains("min CodeHealth", result.Message);
        Assert.Contains("6.8", result.Message);
        Assert.Contains("7.2", result.Message);
    }

    [Fact]
    public void Evaluate_CodeHealth_MinExactlyThreshold_Passes()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.CodeHealth, Summary(minHealth: 7.0), "7", null);
        Assert.Equal(GateOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void Evaluate_CodeHealth_MinAboveThreshold_Passes()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.CodeHealth, Summary(minHealth: 8.5), "7", null);
        Assert.Equal(GateOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void Evaluate_CodeHealth_UsesMinNotAverage()
    {
        // High average but low min must still fail.
        var result = BadgeGate.Evaluate(BadgeMetric.CodeHealth, Summary(avgHealth: 9.5, minHealth: 4.0), "7", null);
        Assert.Equal(GateOutcome.Fail, result.Outcome);
    }

    // --- mi (gates on average) ---

    [Fact]
    public void Evaluate_Mi_AverageBelowThreshold_Fails()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.Mi, Summary(avgMi: 65.0), "70", null);
        Assert.Equal(GateOutcome.Fail, result.Outcome);
        Assert.Contains("average MI", result.Message);
    }

    [Fact]
    public void Evaluate_Mi_AverageExactlyThreshold_Passes()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.Mi, Summary(avgMi: 70.0), "70", null);
        Assert.Equal(GateOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void Evaluate_Mi_AverageAboveThreshold_Passes()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.Mi, Summary(avgMi: 85.0), "70", null);
        Assert.Equal(GateOutcome.Pass, result.Outcome);
    }

    // --- smells (gates on warning count via --fail-over) ---

    [Fact]
    public void Evaluate_Smells_WarningsOverThreshold_Fails()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.Smells, Summary(warnings: 6), null, "5");
        Assert.Equal(GateOutcome.Fail, result.Outcome);
        Assert.Contains("warning smell", result.Message);
    }

    [Fact]
    public void Evaluate_Smells_WarningsExactlyThreshold_Passes()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.Smells, Summary(warnings: 5), null, "5");
        Assert.Equal(GateOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void Evaluate_Smells_WarningsUnderThreshold_Passes()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.Smells, Summary(warnings: 3), null, "5");
        Assert.Equal(GateOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void Evaluate_Smells_CriticalPresent_FailsRegardlessOfThreshold()
    {
        // Warnings within budget, but a critical smell always fails.
        var result = BadgeGate.Evaluate(BadgeMetric.Smells, Summary(warnings: 0, criticals: 1), null, "100");
        Assert.Equal(GateOutcome.Fail, result.Outcome);
        Assert.Contains("critical smell", result.Message);
    }

    [Fact]
    public void Evaluate_Smells_HotPathEscalatedCritical_FailsEvenWithFailOver999()
    {
        // Unity hot-path escalation produces Critical smells that bypass --fail-over entirely.
        var result = BadgeGate.Evaluate(BadgeMetric.Smells, Summary(warnings: 0, criticals: 1), null, "999");
        Assert.Equal(GateOutcome.Fail, result.Outcome);
        Assert.Contains("critical smell", result.Message);
    }

    [Fact]
    public void Evaluate_Smells_ZeroThreshold_AnyWarningFails()
    {
        var passes = BadgeGate.Evaluate(BadgeMetric.Smells, Summary(warnings: 0), null, "0");
        Assert.Equal(GateOutcome.Pass, passes.Outcome);

        var fails = BadgeGate.Evaluate(BadgeMetric.Smells, Summary(warnings: 1), null, "0");
        Assert.Equal(GateOutcome.Fail, fails.Outcome);
    }

    // --- usage errors (incompatible combinations / bad values) ---

    [Theory]
    [InlineData(BadgeMetric.CodeHealth)]
    [InlineData(BadgeMetric.Mi)]
    internal void Evaluate_FailOverOnThresholdMetric_IsUsageError(BadgeMetric metric)
    {
        var result = BadgeGate.Evaluate(metric, Summary(), null, "5");
        Assert.Equal(GateOutcome.UsageError, result.Outcome);
    }

    [Fact]
    public void Evaluate_FailUnderOnSmells_IsUsageError()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.Smells, Summary(), "7", null);
        Assert.Equal(GateOutcome.UsageError, result.Outcome);
    }

    [Fact]
    public void Evaluate_FailUnderNonNumeric_IsUsageError()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.CodeHealth, Summary(), "abc", null);
        Assert.Equal(GateOutcome.UsageError, result.Outcome);
    }

    [Theory]
    [InlineData("xyz")]
    [InlineData("-1")]
    public void Evaluate_FailOverInvalid_IsUsageError(string value)
    {
        var result = BadgeGate.Evaluate(BadgeMetric.Smells, Summary(), null, value);
        Assert.Equal(GateOutcome.UsageError, result.Outcome);
    }

    [Fact]
    public void Evaluate_Energy_AboveThresholdFails()
    {
        var result = BadgeGate.Evaluate(
            BadgeMetric.Energy,
            Summary(hotPathSmells: 3, hotPathMethods: 2),
            null,
            "1.0");

        Assert.Equal(GateOutcome.Fail, result.Outcome);
        Assert.Contains("energy pressure 1.5 > 1", result.Message);
    }

    [Fact]
    public void Evaluate_Energy_AtThresholdPasses()
    {
        var result = BadgeGate.Evaluate(
            BadgeMetric.Energy,
            Summary(hotPathSmells: 2, hotPathMethods: 2),
            null,
            "1.0");

        Assert.Equal(GateOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void Evaluate_Energy_FailUnderIsUsageError()
    {
        var result = BadgeGate.Evaluate(
            BadgeMetric.Energy,
            Summary(hotPathMethods: 1),
            "1.0",
            null);

        Assert.Equal(GateOutcome.UsageError, result.Outcome);
    }

    [Fact]
    public void Evaluate_Energy_NoHotPathMethodsFailsAsUnavailable()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.Energy, Summary(), null, "1.0");

        Assert.Equal(GateOutcome.Fail, result.Outcome);
        Assert.Contains("metric unavailable", result.Message);
        Assert.Contains("hot-path methods", result.Message);
    }

    // --- fail-closed when the metric is unavailable (no data) ---

    [Fact]
    public void Evaluate_CodeHealth_ZeroTypes_FailsAsUnavailable()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.CodeHealth, Summary(typeCount: 0, miBearingCount: 0), "7", null);
        Assert.Equal(GateOutcome.Fail, result.Outcome);
        Assert.Contains("metric unavailable", result.Message);
        Assert.Contains("0 types analyzed", result.Message);
    }

    [Fact]
    public void Evaluate_Mi_ZeroTypes_FailsAsUnavailable()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.Mi, Summary(typeCount: 0, miBearingCount: 0), "70", null);
        Assert.Equal(GateOutcome.Fail, result.Outcome);
        Assert.Contains("metric unavailable", result.Message);
    }

    [Fact]
    public void Evaluate_Smells_ZeroTypes_FailsAsUnavailable()
    {
        var result = BadgeGate.Evaluate(BadgeMetric.Smells, Summary(typeCount: 0, miBearingCount: 0), null, "5");
        Assert.Equal(GateOutcome.Fail, result.Outcome);
        Assert.Contains("metric unavailable", result.Message);
    }

    [Fact]
    public void Evaluate_Mi_NoMethodBearingTypes_FailsAsUnavailable()
    {
        // Types exist (e.g. only records/markers) but none have a defined MI.
        var result = BadgeGate.Evaluate(BadgeMetric.Mi, Summary(typeCount: 5, avgMi: 0.0, miBearingCount: 0), "70", null);
        Assert.Equal(GateOutcome.Fail, result.Outcome);
        Assert.Contains("metric unavailable", result.Message);
        Assert.Contains("method-bearing", result.Message);
    }

    [Fact]
    public void Evaluate_ZeroTypes_NoGateFlags_StillPasses()
    {
        // No gate requested: an empty project must not be forced to fail.
        var result = BadgeGate.Evaluate(BadgeMetric.CodeHealth, Summary(typeCount: 0, miBearingCount: 0), null, null);
        Assert.Equal(GateOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void ValidateOptions_ValidThresholdCombo_Passes()
    {
        Assert.Equal(GateOutcome.Pass, BadgeGate.ValidateOptions(BadgeMetric.CodeHealth, "7", null).Outcome);
        Assert.Equal(GateOutcome.Pass, BadgeGate.ValidateOptions(BadgeMetric.Smells, null, "5").Outcome);
    }

    [Fact]
    public void ValidateOptions_DupFailOverDecimal_Passes()
    {
        Assert.Equal(GateOutcome.Pass, BadgeGate.ValidateOptions(BadgeMetric.Dup, null, "3.5").Outcome);
    }

    [Fact]
    public void Evaluate_Dup_OverThreshold_Fails()
    {
        var result = BadgeGate.Evaluate(
            BadgeMetric.Dup,
            new StatuslineFormatter.Summary(0, 0, 0, 0, 10, 0, 0, 0),
            null,
            "3",
            duplicationPercent: 4.2);
        Assert.Equal(GateOutcome.Fail, result.Outcome);
    }

    [Fact]
    public void Evaluate_Dup_FailUnder_IsUsageError()
    {
        var result = BadgeGate.Evaluate(
            BadgeMetric.Dup,
            new StatuslineFormatter.Summary(0, 0, 0, 0, 10, 0, 0, 0),
            "3",
            null,
            duplicationPercent: 1.0);
        Assert.Equal(GateOutcome.UsageError, result.Outcome);
    }
}
