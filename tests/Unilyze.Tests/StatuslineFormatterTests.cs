namespace Unilyze.Tests;

public sealed class StatuslineFormatterTests
{
    [Fact]
    public void ComputeSummary_EmptyResult_ReturnsZeros()
    {
        var result = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], []);
        var summary = StatuslineFormatter.ComputeSummary(result);

        Assert.Equal(0.0, summary.AverageCodeHealth);
        Assert.Equal(0.0, summary.MinCodeHealth);
        Assert.Equal(0, summary.WarningCount);
        Assert.Equal(0, summary.CriticalCount);
        Assert.Equal(0, summary.TypeCount);
        Assert.Equal(0.0, summary.AverageMaintainabilityIndex);
        Assert.Equal(0, summary.BoxingCount);
        Assert.Equal(0, summary.CyclicDependencyCount);
    }

    [Fact]
    public void ComputeSummary_WithMetrics_ComputesCorrectly()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.GodClass, SmellSeverity.Warning, "A", null, "too big"),
            new(CodeSmellKind.HighComplexity, SmellSeverity.Warning, "A", "M1", "complex"),
            new(CodeSmellKind.GodClass, SmellSeverity.Critical, "A", null, "huge"),
        };
        var metrics = new List<TypeMetrics>
        {
            new("TypeA", "Ns", "Asm", 100, 5, 2, 3.0, 5, 3.0, 5, 0, 8.0, [],
                CodeSmells: smells, AverageMaintainabilityIndex: 70.0, BoxingCount: 3),
            new("TypeB", "Ns", "Asm", 50, 2, 1, 1.0, 2, 1.0, 2, 0, 10.0, [],
                AverageMaintainabilityIndex: 90.0, BoxingCount: 1),
        };
        var result = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], metrics);
        var summary = StatuslineFormatter.ComputeSummary(result);

        Assert.Equal(9.0, summary.AverageCodeHealth);
        Assert.Equal(8.0, summary.MinCodeHealth);
        Assert.Equal(2, summary.WarningCount);
        Assert.Equal(1, summary.CriticalCount);
        Assert.Equal(2, summary.TypeCount);
        Assert.Equal(80.0, summary.AverageMaintainabilityIndex);
        Assert.Equal(4, summary.BoxingCount);
    }

    [Fact]
    public void ComputeSummary_MethodlessTypes_ExcludedFromMiAverage()
    {
        var metrics = new List<TypeMetrics>
        {
            new("TypeA", "Ns", "Asm", 100, 5, 2, 3.0, 5, 3.0, 5, 0, 8.0, [],
                AverageMaintainabilityIndex: 60.0),
            // Method-less record: MI is undefined (null) and must not drag the average down
            new("RecordB", "Ns", "Asm", 10, 0, 0, 0.0, 0, 0.0, 0, 0, 10.0, [],
                AverageMaintainabilityIndex: null),
        };
        var result = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], metrics);
        var summary = StatuslineFormatter.ComputeSummary(result);

        Assert.Equal(60.0, summary.AverageMaintainabilityIndex);
    }

    [Fact]
    public void ComputeSummary_AllTypesMethodless_MiAverageIsZero()
    {
        var metrics = new List<TypeMetrics>
        {
            new("RecordA", "Ns", "Asm", 10, 0, 0, 0.0, 0, 0.0, 0, 0, 10.0, [],
                AverageMaintainabilityIndex: null),
        };
        var result = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], metrics);
        var summary = StatuslineFormatter.ComputeSummary(result);

        Assert.Equal(0.0, summary.AverageMaintainabilityIndex);
    }

    [Fact]
    public void ComputeSummary_EnergyPressure_ExcludesSuppressedTriagedAndBaselinedSmells()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.WeakTemporization, SmellSeverity.Warning, "A", "Update", "active",
                InHotPath: true),
            new(CodeSmellKind.LinqInHotPath, SmellSeverity.Warning, "A", "Update", "suppressed",
                Suppressed: true, InHotPath: true),
            new(CodeSmellKind.BoxingAllocation, SmellSeverity.Critical, "A", "Update", "triaged",
                Triage: "false-positive", InHotPath: true),
            new(CodeSmellKind.ParamsArrayAllocation, SmellSeverity.Critical, "A", "Update", "baselined",
                Baselined: true, InHotPath: true),
        };
        var metrics = new[]
        {
            new TypeMetrics(
                "A", "Ns", "Asm", 20, 2, 0, 0, 0, 1, 1, 0, 10, [],
                CodeSmells: smells,
                HotPathMethodCount: 2),
        };
        var result = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], metrics);

        var actual = StatuslineFormatter.ComputeSummary(result, excludeBaselined: true);

        Assert.Equal(1, actual.HotPathSmellCount);
        Assert.Equal(2, actual.HotPathMethodCount);
    }

    [Fact]
    public void Format_HighHealth_ContainsAllSections()
    {
        var summary = new StatuslineFormatter.Summary(9.4, 8.5, 87, 5, 100, 75.0, 10, 0);
        var output = StatuslineFormatter.Format(summary);

        Assert.Contains("CH:9.4", output);
        Assert.Contains("8.5", output);        // min health
        Assert.Contains("MI:75", output);
        Assert.Contains("87smells", output);
        Assert.Contains("\U0001f5345", output); // 🔴5
        Assert.Contains("\U0001f4e610", output); // 📦10
        Assert.DoesNotContain("\u267b", output); // no cyclic
    }

    [Fact]
    public void Format_MediumHealth_YellowColor()
    {
        var summary = new StatuslineFormatter.Summary(6.0, 5.0, 10, 0, 50, 65.0, 0, 0);
        var output = StatuslineFormatter.Format(summary);

        Assert.Contains("\x1b[33mCH:6.0", output); // Yellow for health
        Assert.DoesNotContain("\U0001f534", output); // No criticals
        Assert.DoesNotContain("\U0001f4e6", output); // No boxing
    }

    [Fact]
    public void Format_LowHealth_RedColor()
    {
        var summary = new StatuslineFormatter.Summary(3.2, 1.5, 200, 15, 200, 40.0, 50, 3);
        var output = StatuslineFormatter.Format(summary);

        Assert.Contains("\x1b[31mCH:3.2", output);  // Red for health
        Assert.Contains("\x1b[31mMI:40", output);    // Red for MI
        Assert.Contains("\U0001f53415", output);      // 🔴15
        Assert.Contains("\u267b3", output);            // ♻3 cyclic
    }

    [Fact]
    public void Format_ZeroCriticals_HidesCriticalSection()
    {
        var summary = new StatuslineFormatter.Summary(8.5, 7.0, 10, 0, 50, 80.0, 0, 0);
        var output = StatuslineFormatter.Format(summary);

        Assert.DoesNotContain("\U0001f534", output);
    }

    [Fact]
    public void Format_ZeroTypes_ReturnsEmpty()
    {
        var summary = new StatuslineFormatter.Summary(0.0, 0.0, 0, 0, 0, 0.0, 0, 0);
        var output = StatuslineFormatter.Format(summary);

        Assert.Equal("", output);
    }

    [Theory]
    [InlineData("SyntaxOnly", "[syntax]")]
    [InlineData("CoreEngine", "[core]")]
    [InlineData("FullEngine", "[full]")]
    public void Format_BelowComplete_AppendsLevelMarker(string level, string expectedMarker)
    {
        var summary = new StatuslineFormatter.Summary(9.0, 8.0, 0, 0, 10, 80.0, 0, 0, AnalysisLevel: level);
        var output = StatuslineFormatter.Format(summary);

        Assert.Contains(expectedMarker, output);
    }

    [Fact]
    public void Format_CompleteLevel_NoMarker()
    {
        var summary = new StatuslineFormatter.Summary(9.0, 8.0, 0, 0, 10, 80.0, 0, 0, AnalysisLevel: "Complete");
        var output = StatuslineFormatter.Format(summary);

        Assert.DoesNotContain("[syntax]", output);
        Assert.DoesNotContain("[core]", output);
        Assert.DoesNotContain("[full]", output);
    }

    [Fact]
    public void Format_NullLevel_NoMarker()
    {
        var summary = new StatuslineFormatter.Summary(9.0, 8.0, 0, 0, 10, 80.0, 0, 0, AnalysisLevel: null);
        var output = StatuslineFormatter.Format(summary);

        // The output contains ANSI color escapes (which include '['), so assert against the
        // specific level markers rather than any '[' character.
        Assert.DoesNotContain("[syntax]", output);
        Assert.DoesNotContain("[core]", output);
        Assert.DoesNotContain("[full]", output);
    }

    [Fact]
    public void ComputeSummary_CapturesAnalysisLevel()
    {
        var metrics = new List<TypeMetrics>
        {
            new("TypeA", "Ns", "Asm", 100, 5, 2, 3.0, 5, 3.0, 5, 0, 8.0, []),
        };
        var result = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], metrics, "SyntaxOnly");
        var summary = StatuslineFormatter.ComputeSummary(result);

        Assert.Equal("SyntaxOnly", summary.AnalysisLevel);
    }
}
