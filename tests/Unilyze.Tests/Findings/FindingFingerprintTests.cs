using System.Text.Json.Nodes;

namespace Unilyze.Tests.Findings;

public sealed class FindingFingerprintTests
{
    static AnalysisResult MakeResult(IReadOnlyList<TypeMetrics> typeMetrics, string projectPath = "/project")
        => new(projectPath, DateTimeOffset.UtcNow, [], [], [], typeMetrics);

    [Fact]
    public void AssignIds_JsonAndSarifFingerprintsMatch()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.GodClass, SmellSeverity.Warning, "BigClass", null, "600 lines")
        };
        var typeMetrics = new TypeMetrics(
            "BigClass", "TestNs", "TestAssembly",
            600, 25, 1, 3.0, 5, 3.0, 5, 0, 8.0,
            [],
            CodeSmells: smells,
            FilePath: "/project/src/BigClass.cs",
            StartLine: 10);
        var result = MakeResult([typeMetrics]);

        var withIds = FindingFingerprint.AssignIds(result);
        var smell = withIds.TypeMetrics![0].CodeSmells![0];
        Assert.False(string.IsNullOrEmpty(smell.Id));

        var sarif = SarifFormatter.Generate(withIds);
        var doc = JsonNode.Parse(sarif)!;
        var fingerprint = doc["runs"]![0]!["results"]![0]!["partialFingerprints"]![
            SarifFormatter.FingerprintKey]!.GetValue<string>();

        Assert.Equal(smell.Id, fingerprint);
    }
}
