namespace Unilyze.Tests.Findings;

public sealed class TriageMatcherTests
{
    static AnalysisResult CreateResult(params CodeSmell[] smells)
    {
        var metrics = new List<TypeMetrics>
        {
            new(
                TypeName: "Greeter",
                Namespace: "",
                Assembly: "App",
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
                CodeSmells: smells,
                TypeId: "App::Greeter")
        };
        return new AnalysisResult("/", DateTimeOffset.UtcNow, [], [], [], metrics);
    }

    static CodeSmell Smell(string id, CodeSmellKind kind = CodeSmellKind.LongMethod)
        => new(kind, SmellSeverity.Warning, "Greeter", "Run", "msg", Id: id);

    static TriageFile Triage(params TriageEntry[] entries)
        => new(
            TriageFile.CurrentSchemaVersion,
            "0.3.0",
            AnalysisResult.CurrentMetricsVersion,
            entries);

    [Fact]
    public void Apply_MatchedSmell_GainsTriageField()
    {
        var id = "abc123";
        var result = CreateResult(Smell(id));
        var triage = Triage(new TriageEntry(id, TriageVerdicts.FalsePositive, "verified"));

        var updated = TriageMatcher.Apply(result, triage, out var stats);

        Assert.Equal(1, stats.MatchedCount);
        Assert.Equal(1, stats.FalsePositiveCount);
        Assert.Equal(TriageVerdicts.FalsePositive, updated.TypeMetrics![0].CodeSmells![0].Triage);
    }

    [Fact]
    public void Apply_StaleEntry_CountsButDoesNotMatch()
    {
        var result = CreateResult(Smell("live-id"));
        var triage = Triage(
            new TriageEntry("live-id", TriageVerdicts.Confirmed),
            new TriageEntry("stale-id", TriageVerdicts.FalsePositive));

        TriageMatcher.Apply(result, triage, out var stats);

        Assert.Equal(1, stats.MatchedCount);
        Assert.Equal(1, stats.StaleCount);
    }
}
