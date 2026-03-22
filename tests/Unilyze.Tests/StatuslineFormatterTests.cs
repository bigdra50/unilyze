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
}
