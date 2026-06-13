namespace Unilyze.Tests.Findings;

public sealed class BaselineMatcherTests
{
    static AnalysisResult CreateResult(params (string TypeId, CodeSmell[] Smells)[] types)
    {
        var metrics = types.Select(type => new TypeMetrics(
            TypeName: type.TypeId.Split("::").Last().Split('.').Last(),
            Namespace: "",
            Assembly: type.TypeId.Split("::")[0],
            LineCount: 100,
            MethodCount: 1,
            MaxNestingDepth: 1,
            AverageCognitiveComplexity: 1,
            MaxCognitiveComplexity: 1,
            AverageCyclomaticComplexity: 1,
            MaxCyclomaticComplexity: 1,
            ExcessiveParameterMethodCount: 0,
            CodeHealth: 5,
            Methods: [],
            CodeSmells: type.Smells,
            TypeId: type.TypeId)).ToList();

        return new AnalysisResult("/", DateTimeOffset.UtcNow, [], [], [], metrics);
    }

    static CodeSmell Smell(CodeSmellKind kind, string? method = "M", SmellSeverity severity = SmellSeverity.Warning)
        => new(kind, severity, "T", method, "msg");

    static BaselineFile Baseline(params BaselineFingerprintEntry[] entries)
        => new(
            BaselineFile.CurrentSchemaVersion,
            "0.3.0",
            AnalysisResult.CurrentMetricsVersion,
            DateTimeOffset.UtcNow,
            entries);

    [Fact]
    public void Apply_IdempotentWhenUnchanged_SuppressesAllKnownSmells()
    {
        var typeId = "App::Sample.Greeter";
        var result = CreateResult((typeId, [Smell(CodeSmellKind.LongMethod)]));
        var baseline = BaselineFile.FromAnalysis(result);

        var updated = BaselineMatcher.Apply(result, baseline, out var stats);

        Assert.Equal(1, stats.SuppressedCount);
        Assert.Equal(0, stats.NewCount);
        Assert.Equal(1, updated.SuppressedCount);
        Assert.All(updated.TypeMetrics![0].CodeSmells!, smell => Assert.True(smell.Baselined));
    }

    [Fact]
    public void Apply_ExtraSameKindSmell_ReportsOnlyOverflowAsNew()
    {
        var typeId = "App::Sample.Greeter";
        var baseline = Baseline(new BaselineFingerprintEntry(
            typeId, CodeSmellKind.BoxingAllocation, "Hot", 1, SmellSeverity.Warning));

        var result = CreateResult((typeId,
        [
            Smell(CodeSmellKind.BoxingAllocation, "Hot"),
            Smell(CodeSmellKind.BoxingAllocation, "Hot"),
        ]));

        var updated = BaselineMatcher.Apply(result, baseline, out var stats);

        Assert.Equal(1, stats.SuppressedCount);
        Assert.Equal(1, stats.NewCount);
        Assert.Equal(1, updated.TypeMetrics![0].CodeSmells!.Count(s => s.Baselined == true));
        Assert.Equal(1, updated.TypeMetrics![0].CodeSmells!.Count(s => s.Baselined != true));
    }

    [Fact]
    public void Apply_SeverityEscalation_ReportsCriticalAsNew()
    {
        var typeId = "App::Sample.Greeter";
        var baseline = Baseline(new BaselineFingerprintEntry(
            typeId, CodeSmellKind.LongMethod, "Huge", 1, SmellSeverity.Warning));

        var result = CreateResult((typeId, [Smell(CodeSmellKind.LongMethod, "Huge", SmellSeverity.Critical)]));

        var updated = BaselineMatcher.Apply(result, baseline, out var stats);

        Assert.Equal(0, stats.SuppressedCount);
        Assert.Equal(1, stats.NewCount);
        Assert.Null(updated.TypeMetrics![0].CodeSmells![0].Baselined);
    }

    [Fact]
    public void Apply_FixedEntry_CountsBaselineKeysWithNoCurrentSmells()
    {
        var typeId = "App::Sample.Greeter";
        var baseline = Baseline(
            new BaselineFingerprintEntry(typeId, CodeSmellKind.LongMethod, "Old", 1, SmellSeverity.Warning),
            new BaselineFingerprintEntry(typeId, CodeSmellKind.GodClass, null, 1, SmellSeverity.Warning));

        var result = CreateResult((typeId, [Smell(CodeSmellKind.LongMethod, "New")]));

        _ = BaselineMatcher.Apply(result, baseline, out var stats);

        Assert.Equal(2, stats.FixedEntryCount);
    }

    [Fact]
    public void FromAnalysis_GroupsByTypeKindMethodAndCount()
    {
        var typeId = "App::Sample.Greeter";
        var result = CreateResult((typeId,
        [
            Smell(CodeSmellKind.BoxingAllocation, "Hot"),
            Smell(CodeSmellKind.BoxingAllocation, "Hot"),
            Smell(CodeSmellKind.LongMethod, "Other"),
        ]));

        var baseline = BaselineFile.FromAnalysis(result);

        Assert.Equal(2, baseline.Fingerprints.Count);
        Assert.Contains(baseline.Fingerprints, entry =>
            entry.Kind == CodeSmellKind.BoxingAllocation && entry.MethodName == "Hot" && entry.Count == 2);
    }
}
