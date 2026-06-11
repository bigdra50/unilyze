using System.Text.Json;
using System.Text.Json.Nodes;

namespace Unilyze.Tests;

public sealed class InlineSuppressionTests : IDisposable
{
    readonly string _tempDir;

    public InlineSuppressionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-inline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDir))
            return;

        foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Build_NoDirectives_LeavesSuppressedCountAbsent()
    {
        WriteProject("""
            namespace Sample;

            public class Plain
            {
                public int Add(int a, int b) => a + b;
            }
            """);

        var result = AnalysisPipeline.Build(_tempDir, null, null);
        Assert.Null(result.SuppressedCount);
        Assert.All(result.TypeMetrics ?? [], type =>
            Assert.All(type.CodeSmells ?? [], smell => Assert.Null(smell.Suppressed)));
    }

    [Fact]
    public void Build_DisableNextLine_SuppressesOnlyTargetedDetectorSmell()
    {
        WriteProject("""
            namespace Sample;

            public class Sample
            {
                void Guarded()
                {
                    try { System.Console.WriteLine(); }
                    // unilyze-disable-next-line UNI014 -- top-level guard, intentional
                    catch { }
                }

                void Reported()
                {
                    try { System.Console.WriteLine(); }
                    catch { }
                }
            }
            """);

        var result = AnalysisPipeline.Build(_tempDir, null, null);
        var smells = result.TypeMetrics!.Single().CodeSmells!;

        Assert.Equal(1, result.SuppressedCount);
        Assert.Single(smells, s => s.Suppressed == true && s.Kind == CodeSmellKind.CatchAllException);
        Assert.Single(smells, s => s.Suppressed != true && s.Kind == CodeSmellKind.CatchAllException);
        Assert.Equal("top-level guard, intentional",
            smells.Single(s => s.Suppressed == true).SuppressionJustification);
    }

    [Fact]
    public void Build_DisableOnMethod_SuppressesMetricSmellForMethodScope()
    {
        WriteProject($$"""
            namespace Sample;

            public class SmellyType
            {
            {{BuildLongMethod("MeasuredLongMethod", "// unilyze-disable UNI002")}}
            {{BuildLongMethod("StillReported", "")}}
            }
            """);

        var result = AnalysisPipeline.Build(_tempDir, null, null);
        var smells = result.TypeMetrics!.Single().CodeSmells!
            .Where(s => s.Kind == CodeSmellKind.LongMethod)
            .ToList();

        Assert.Equal(1, smells.Count(s => s.Suppressed == true));
        Assert.Equal(1, smells.Count(s => s.Suppressed != true));
        Assert.Equal("MeasuredLongMethod", smells.Single(s => s.Suppressed == true).MethodName);
    }

    [Fact]
    public void Sarif_SuppressedSmell_EmitsInSourceSuppression()
    {
        WriteProject("""
            namespace Sample;

            public class Sample
            {
                void Guarded()
                {
                    try { System.Console.WriteLine(); }
                    // unilyze-disable-next-line UNI014 -- reason text
                    catch { }
                }
            }
            """);

        var result = AnalysisPipeline.Build(_tempDir, null, null);
        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var suppressions = doc["runs"]![0]!["results"]!
            .AsArray()
            .Select(r => r!["suppressions"])
            .Where(s => s is not null)
            .SelectMany(s => s!.AsArray())
            .Select(s => s!["kind"]!.GetValue<string>())
            .ToList();

        Assert.Contains("inSource", suppressions);
    }

    [Fact]
    public void ComputeSummary_ExcludesInlineSuppressedSmells()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.GodClass, SmellSeverity.Warning, "A", null, "big", Suppressed: true),
            new(CodeSmellKind.HighComplexity, SmellSeverity.Warning, "A", "M1", "complex"),
        };
        var metrics = new List<TypeMetrics>
        {
            new("TypeA", "Ns", "Asm", 100, 5, 2, 3.0, 5, 3.0, 5, 0, 8.0, [], CodeSmells: smells),
        };
        var result = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], metrics);
        var summary = StatuslineFormatter.ComputeSummary(result);

        Assert.Equal(1, summary.WarningCount);
    }

    [Fact]
    public void DiffCalculator_ExcludesInlineSuppressedSmells()
    {
        var smell1 = new CodeSmell(CodeSmellKind.LongMethod, SmellSeverity.Warning, "T", "M1", "msg");
        var smell2 = new CodeSmell(CodeSmellKind.LongMethod, SmellSeverity.Warning, "T", "M2", "msg2", Suppressed: true);

        var before = new TypeMetrics(
            "T", "", "Asm", 10, 2, 1, 1, 1, 1, 1, 0, 8, [], CodeSmells: [smell1]);
        var after = before with { CodeSmells = [smell1, smell2] };

        var diff = DiffCalculator.Compare(
            new AnalysisResult("/", DateTimeOffset.UtcNow, [], [], [], [before]),
            new AnalysisResult("/", DateTimeOffset.UtcNow, [], [], [], [after]));

        Assert.Null(diff.Unchanged[0].SmellChanges);
    }

    [Fact]
    public void BaselineMatcher_SkipsInlineSuppressed_DoesNotDoubleCount()
    {
        var typeId = "App::Sample.Greeter";
        var inlineSuppressed = new CodeSmell(
            CodeSmellKind.LongMethod, SmellSeverity.Warning, "Greeter", "Huge", "msg", Suppressed: true);
        var result = new AnalysisResult(
            "/",
            DateTimeOffset.UtcNow,
            [],
            [],
            [],
            [new TypeMetrics(
                "Greeter", "", "App", 100, 1, 1, 1, 1, 1, 1, 0, 5, [], CodeSmells: [inlineSuppressed],
                TypeId: typeId)],
            SuppressedCount: 1);

        var baseline = new BaselineFile(
            BaselineFile.CurrentSchemaVersion,
            "0.3.0",
            AnalysisResult.CurrentMetricsVersion,
            DateTimeOffset.UtcNow,
            [new BaselineFingerprintEntry(typeId, CodeSmellKind.LongMethod, "Huge", 1, SmellSeverity.Warning)]);

        var updated = BaselineMatcher.Apply(result, baseline, out var stats);

        Assert.Equal(0, stats.SuppressedCount);
        Assert.Equal(1, updated.SuppressedCount);
        Assert.True(updated.TypeMetrics![0].CodeSmells![0].Suppressed);
        Assert.Null(updated.TypeMetrics[0].CodeSmells[0].Baselined);
    }

    static string BuildLongMethod(string methodName, string leadingComment)
    {
        var body = string.Join("\n", Enumerable.Range(1, 85).Select(i => $"        x += {i};"));
        var commentLine = string.IsNullOrEmpty(leadingComment) ? "" : $"    {leadingComment}\n";
        return $$"""
            {{commentLine}}    public int {{methodName}}(int seed)
                {
                    var x = seed;
            {{body}}
                    return x;
                }
            """;
    }

    void WriteProject(string source)
    {
        File.WriteAllText(Path.Combine(_tempDir, "Sample.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk" />
            """);
        File.WriteAllText(Path.Combine(_tempDir, "Sample.cs"), source);
    }
}
