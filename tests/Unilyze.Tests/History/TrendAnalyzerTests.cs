using System.Text.Json;

namespace Unilyze.Tests.History;

public class TrendAnalyzerTests
{
    static AnalysisResult MakeResult(
        DateTimeOffset analyzedAt,
        string projectPath = "/project",
        IReadOnlyList<TypeMetrics>? typeMetrics = null)
    {
        return new AnalysisResult(
            projectPath,
            analyzedAt,
            [],
            [],
            [],
            typeMetrics);
    }

    static TypeMetrics MakeTypeMetrics(
        string typeName = "TestClass",
        string ns = "TestNs",
        string assembly = "TestAssembly",
        double codeHealth = 8.0,
        double avgCogCC = 3.0,
        IReadOnlyList<CodeSmell>? smells = null,
        int? hotPathMethodCount = null)
    {
        return new TypeMetrics(
            typeName, ns, assembly,
            100, 5, 2,
            avgCogCC, 5, 3.0, 5,
            0, codeHealth,
            [],
            CodeSmells: smells,
            HotPathMethodCount: hotPathMethodCount);
    }

    // --- ToSnapshot ---

    [Fact]
    public void ToSnapshot_ExtractsCorrectValues()
    {
        var types = new[]
        {
            MakeTypeMetrics(codeHealth: 8.0, avgCogCC: 3.0),
            MakeTypeMetrics(typeName: "Other", codeHealth: 6.0, avgCogCC: 5.0),
        };
        var result = MakeResult(
            new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero),
            typeMetrics: types);

        var snapshot = TrendAnalyzer.ToSnapshot(result);

        Assert.Equal(2, snapshot.TypeCount);
        Assert.Equal(7.0, snapshot.AverageCodeHealth);  // (8+6)/2
        Assert.Equal(6.0, snapshot.MinCodeHealth);
        Assert.Equal(4.0, snapshot.AverageCognitiveComplexity);  // (3+5)/2
    }

    [Fact]
    public void ToSnapshot_CountsCodeSmells()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.GodClass, SmellSeverity.Critical, "TestClass", null, "too big"),
            new(CodeSmellKind.HighComplexity, SmellSeverity.Warning, "TestClass", null, "complex"),
        };
        var types = new[]
        {
            MakeTypeMetrics(smells: smells),
            MakeTypeMetrics(typeName: "Clean"),
        };
        var result = MakeResult(DateTimeOffset.UtcNow, typeMetrics: types);

        var snapshot = TrendAnalyzer.ToSnapshot(result);

        Assert.Equal(2, snapshot.CodeSmellCount);
        Assert.Equal(1, snapshot.WarningSmellCount);
        Assert.Equal(1, snapshot.CriticalSmellCount);
    }

    [Fact]
    public void ToSnapshot_CapturesEnergyCountsUsingTrendFilters()
    {
        var smells = new[]
        {
            new CodeSmell(
                CodeSmellKind.WeakTemporization,
                SmellSeverity.Warning,
                "TestClass",
                "Update",
                "active",
                InHotPath: true),
            new CodeSmell(
                CodeSmellKind.LinqInHotPath,
                SmellSeverity.Warning,
                "TestClass",
                "Update",
                "suppressed",
                Suppressed: true,
                InHotPath: true),
        };
        var result = MakeResult(
            DateTimeOffset.UtcNow,
            typeMetrics: [MakeTypeMetrics(smells: smells, hotPathMethodCount: 2)]);

        var snapshot = TrendAnalyzer.ToSnapshot(result);

        Assert.Equal(1, snapshot.HotPathSmellCount);
        Assert.Equal(2, snapshot.HotPathMethodCount);
    }

    [Fact]
    public void JsonDeserialization_OldTrendSnapshot_DefaultsEnergyCountsToZero()
    {
        const string json = """
            {
              "snapshots": [{
                "analyzedAt": "2025-01-01T00:00:00+00:00",
                "projectPath": "/project",
                "typeCount": 1,
                "averageCodeHealth": 8,
                "minCodeHealth": 8,
                "codeSmellCount": 0,
                "highComplexityTypeCount": 0,
                "averageCognitiveComplexity": 1
              }],
              "summary": {
                "snapshotCount": 1,
                "codeHealthDelta": 0,
                "codeSmellDelta": 0
              }
            }
            """;

        var actual = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.TrendResult);

        Assert.NotNull(actual);
        Assert.Equal(0, actual.Snapshots[0].HotPathSmellCount);
        Assert.Equal(0, actual.Snapshots[0].HotPathMethodCount);
    }

    [Fact]
    public void ToSnapshot_WithSourceFile_PopulatesProvenanceFields()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.GodClass, SmellSeverity.Warning, "TestClass", null, "big"),
        };
        var result = new AnalysisResult(
            "/project",
            DateTimeOffset.UtcNow,
            [], [], [],
            [MakeTypeMetrics(smells: smells)],
            MetricsVersion: 3,
            Profile: "unity");

        var snapshot = TrendAnalyzer.ToSnapshot(result, "2026-06-01.json");

        Assert.Equal("2026-06-01.json", snapshot.SourceFile);
        Assert.Equal(3, snapshot.MetricsVersion);
        Assert.Equal("unity", snapshot.Profile);
        Assert.Equal(1, snapshot.WarningSmellCount);
        Assert.Equal(0, snapshot.CriticalSmellCount);
    }

    [Fact]
    public void ToSnapshot_WithoutSourceFile_LeavesProvenanceDefaults()
    {
        var result = MakeResult(DateTimeOffset.UtcNow, typeMetrics: [MakeTypeMetrics()]);

        var snapshot = TrendAnalyzer.ToSnapshot(result);

        Assert.Null(snapshot.SourceFile);
        Assert.Equal(0, snapshot.MetricsVersion);
        Assert.Null(snapshot.Profile);
    }

    [Fact]
    public void ToSnapshot_NoTypeMetrics_AllZeros()
    {
        var result = MakeResult(DateTimeOffset.UtcNow, typeMetrics: []);

        var snapshot = TrendAnalyzer.ToSnapshot(result);

        Assert.Equal(0, snapshot.TypeCount);
        Assert.Equal(0.0, snapshot.AverageCodeHealth);
        Assert.Equal(0.0, snapshot.MinCodeHealth);
        Assert.Equal(0, snapshot.CodeSmellCount);
        Assert.Equal(0, snapshot.HighComplexityTypeCount);
        Assert.Equal(0.0, snapshot.AverageCognitiveComplexity);
    }

    [Fact]
    public void ToSnapshot_HighComplexityCount()
    {
        var types = new[]
        {
            MakeTypeMetrics(typeName: "Healthy", codeHealth: 8.0),
            MakeTypeMetrics(typeName: "Unhealthy1", codeHealth: 3.5),
            MakeTypeMetrics(typeName: "Unhealthy2", codeHealth: 2.0),
            MakeTypeMetrics(typeName: "Border", codeHealth: 4.0),
        };
        var result = MakeResult(DateTimeOffset.UtcNow, typeMetrics: types);

        var snapshot = TrendAnalyzer.ToSnapshot(result);

        // CodeHealth < 4.0 => Unhealthy1, Unhealthy2
        Assert.Equal(2, snapshot.HighComplexityTypeCount);
    }

    // --- Analyze ---

    [Fact]
    public void AnalyzeSnapshots_PopulatesSourceFileOnSnapshots()
    {
        var results = new[]
        {
            (SourceFile: (string?)"a.json", Result: MakeResult(
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                typeMetrics: [MakeTypeMetrics(codeHealth: 7.0)])),
            (SourceFile: (string?)"b.json", Result: MakeResult(
                new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero),
                typeMetrics: [MakeTypeMetrics(codeHealth: 8.0)])),
        };

        var trend = TrendAnalyzer.AnalyzeSnapshots(results);

        Assert.Equal("a.json", trend.Snapshots[0].SourceFile);
        Assert.Equal("b.json", trend.Snapshots[1].SourceFile);
    }

    [Fact]
    public void Analyze_EmptyList_ReturnsZeroDelta()
    {
        var trend = TrendAnalyzer.Analyze([]);

        Assert.Empty(trend.Snapshots);
        Assert.Equal(0, trend.Summary.SnapshotCount);
        Assert.Equal(0.0, trend.Summary.CodeHealthDelta);
        Assert.Equal(0, trend.Summary.CodeSmellDelta);
    }

    [Fact]
    public void Analyze_SingleSnapshot_DeltaZero()
    {
        var results = new[]
        {
            MakeResult(
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                typeMetrics: [MakeTypeMetrics(codeHealth: 7.0)])
        };

        var trend = TrendAnalyzer.Analyze(results);

        Assert.Single(trend.Snapshots);
        Assert.Equal(1, trend.Summary.SnapshotCount);
        Assert.Equal(0.0, trend.Summary.CodeHealthDelta);
        Assert.Equal(0, trend.Summary.CodeSmellDelta);
    }

    [Fact]
    public void Analyze_MultipleSnapshots_CorrectDelta()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.GodClass, SmellSeverity.Critical, "TestClass", null, "big"),
        };
        var results = new[]
        {
            MakeResult(
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                typeMetrics: [MakeTypeMetrics(codeHealth: 5.0, smells: smells)]),
            MakeResult(
                new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero),
                typeMetrics: [MakeTypeMetrics(codeHealth: 8.0)]),
        };

        var trend = TrendAnalyzer.Analyze(results);

        Assert.Equal(2, trend.Summary.SnapshotCount);
        Assert.Equal(3.0, trend.Summary.CodeHealthDelta);  // 8.0 - 5.0
        Assert.Equal(-1, trend.Summary.CodeSmellDelta);     // 0 - 1
    }

    [Fact]
    public void Analyze_SnapshotsSortedByAnalyzedAt()
    {
        var results = new[]
        {
            MakeResult(
                new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero),
                typeMetrics: [MakeTypeMetrics()]),
            MakeResult(
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                typeMetrics: [MakeTypeMetrics()]),
            MakeResult(
                new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero),
                typeMetrics: [MakeTypeMetrics()]),
        };

        var trend = TrendAnalyzer.Analyze(results);

        Assert.Equal(3, trend.Snapshots.Count);
        Assert.True(trend.Snapshots[0].AnalyzedAt < trend.Snapshots[1].AnalyzedAt);
        Assert.True(trend.Snapshots[1].AnalyzedAt < trend.Snapshots[2].AnalyzedAt);
    }

    [Fact]
    public void Analyze_DeltaUsesFirstAndLastAfterSort()
    {
        // Provide results out of order to verify sort-then-delta
        var results = new[]
        {
            MakeResult(
                new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero),
                typeMetrics: [MakeTypeMetrics(codeHealth: 9.0)]),
            MakeResult(
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                typeMetrics: [MakeTypeMetrics(codeHealth: 6.0)]),
            MakeResult(
                new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero),
                typeMetrics: [MakeTypeMetrics(codeHealth: 7.0)]),
        };

        var trend = TrendAnalyzer.Analyze(results);

        // First (Jan): 6.0, Last (Dec): 9.0 => delta = 3.0
        Assert.Equal(3.0, trend.Summary.CodeHealthDelta);
    }

    // --- JSON Serialization ---

    [Fact]
    public void JsonSerialization_TrendResult()
    {
        var trend = new TrendResult(
            [
                new TrendSnapshot(
                    new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    "/project", 10, 7.5, 3.2, 5, 2, 4.1),
                new TrendSnapshot(
                    new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero),
                    "/project", 12, 8.0, 4.0, 3, 1, 3.5),
            ],
            new TrendSummary(2, 0.5, -2));

        var json = JsonSerializer.Serialize(trend, AnalysisJsonContext.Default.TrendResult);
        var parsed = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.TrendResult);

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Snapshots.Count);
        Assert.Equal(2, parsed.Summary.SnapshotCount);
        Assert.Equal(0.5, parsed.Summary.CodeHealthDelta);
        Assert.Equal(-2, parsed.Summary.CodeSmellDelta);
        Assert.Equal("/project", parsed.Snapshots[0].ProjectPath);
        Assert.Equal(10, parsed.Snapshots[0].TypeCount);
        Assert.Equal(12, parsed.Snapshots[1].TypeCount);
    }
}
