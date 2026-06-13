using System.Text.Json;

namespace Unilyze.Tests.Query;

public sealed class QueryEvidenceTests
{
    static readonly string GoldenFixturePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "golden", "expected.json"));

    [Fact]
    public void SelectWorst_OrdersByCodeHealthAscending()
    {
        var analysis = LoadGoldenAnalysis();
        var metrics = analysis.TypeMetrics!;

        var selection = QuerySelector.SelectWorst(metrics, 5);

        Assert.Equal(5, selection.Types.Count);
        Assert.True(IsSortedAscending(selection.Types, t => t.CodeHealth));
        Assert.Equal(
            metrics.OrderBy(t => t.CodeHealth).Take(5).Select(t => t.TypeName).ToList(),
            selection.Types.Select(t => t.TypeName).ToList());
    }

    [Fact]
    public void SelectByName_ResolvesQualifiedName()
    {
        var analysis = LoadGoldenAnalysis();
        var target = analysis.TypeMetrics!.First(t => t.TypeName == "GodClassTarget");

        var selection = QuerySelector.SelectByName(analysis.TypeMetrics!, "GoldenFixture.GodClassTarget");

        Assert.Single(selection.Types);
        Assert.Equal(target.TypeName, selection.Types[0].TypeName);
        Assert.Null(selection.AmbiguityMessage);
    }

    [Fact]
    public void SelectByName_AmbiguousName_ReturnsCandidates()
    {
        var metrics = new List<TypeMetrics>
        {
            CreateType("Foo", "Alpha", 5.0),
            CreateType("Foo", "Beta", 6.0),
        };

        var selection = QuerySelector.SelectByName(metrics, "Foo");

        Assert.Empty(selection.Types);
        Assert.Contains("Ambiguous type name 'Foo'", selection.AmbiguityMessage);
        Assert.Contains("Alpha.Foo", selection.AmbiguityMessage);
        Assert.Contains("Beta.Foo", selection.AmbiguityMessage);
    }

    [Fact]
    public void Assembler_IncludesAnchorsForTypeSmellsAndMethods()
    {
        var analysis = LoadGoldenAnalysis();
        var type = analysis.TypeMetrics!.First(t => t.TypeName == "DeepNestingTarget");
        var result = QueryEvidenceAssembler.Build(analysis, [type]);
        var pack = Assert.Single(result.Types);

        Assert.NotNull(pack.Anchor);
        Assert.Contains(":", pack.Anchor);
        Assert.All(pack.Smells, s =>
        {
            Assert.NotNull(s.Anchor);
            Assert.Contains(":", s.Anchor!);
        });
        Assert.All(pack.TopMethods.Where(m => m.Anchor != null), m => Assert.Contains(":", m.Anchor!));
    }

    [Fact]
    public void MarkdownFormatter_IncludesTypeNameCodeHealthAndSmells()
    {
        var analysis = LoadGoldenAnalysis();
        var selection = QuerySelector.SelectWorst(analysis.TypeMetrics!, 3);
        var queryResult = QueryEvidenceAssembler.Build(analysis, selection.Types);
        var markdown = QueryEvidenceFormatter.ToMarkdown(queryResult);

        Assert.Contains("# Query Evidence Pack", markdown);
        Assert.Contains("CH ", markdown);
        Assert.Contains("### Smells", markdown);
        Assert.Contains("DeepNestingTarget", markdown);
    }

    [Fact]
    public void JsonFormatter_OutputsValidCompactJson()
    {
        var analysis = LoadGoldenAnalysis();
        var selection = QuerySelector.SelectWorst(analysis.TypeMetrics!, 2);
        var queryResult = QueryEvidenceAssembler.Build(analysis, selection.Types);
        var json = QueryEvidenceFormatter.ToJson(queryResult);

        Assert.DoesNotContain('\n', json);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("types").ValueKind);
    }

    [Fact]
    public void Assembler_WithApiSurface_IncludesSurfaceOnPack()
    {
        var analysis = LoadGoldenAnalysis() with
        {
            ApiSurface =
            [
                new TypeApiSurface(
                    "Assembly-CSharp::GoldenFixture.GodClassTarget",
                    "GoldenFixture.GodClassTarget",
                    true,
                    "A god class",
                    ["public class GodClassTarget"],
                    ["GodClassTarget", "M1"],
                    0,
                    1)
            ]
        };
        var type = analysis.TypeMetrics!.First(t => t.TypeName == "GodClassTarget");
        var queryResult = QueryEvidenceAssembler.Build(analysis, [type], includeApiSurface: true);
        var pack = Assert.Single(queryResult.Types);

        Assert.NotNull(pack.ApiSurface);
        Assert.True(pack.ApiSurface!.HasDocComment);
        Assert.Equal("A god class", pack.ApiSurface.DocSummary);
    }

    [Fact]
    public void MarkdownFormatter_WithApiSurface_IncludesSection()
    {
        var analysis = LoadGoldenAnalysis() with
        {
            ApiSurface =
            [
                new TypeApiSurface(
                    "Assembly-CSharp::GoldenFixture.DeepNestingTarget",
                    "GoldenFixture.DeepNestingTarget",
                    false,
                    null,
                    ["public class DeepNestingTarget"],
                    ["DeepNestingTarget"],
                    0,
                    0)
            ]
        };
        var type = analysis.TypeMetrics!.First(t => t.TypeName == "DeepNestingTarget");
        var queryResult = QueryEvidenceAssembler.Build(analysis, [type], includeApiSurface: true);
        var markdown = QueryEvidenceFormatter.ToMarkdown(queryResult);

        Assert.Contains("### API Surface", markdown);
        Assert.Contains("#### Identifiers", markdown);
    }

    static AnalysisResult LoadGoldenAnalysis()
    {
        var json = File.ReadAllText(GoldenFixturePath);
        return JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.AnalysisResult)
               ?? throw new InvalidOperationException("Failed to load golden fixture");
    }

    static TypeMetrics CreateType(string name, string ns, double codeHealth) =>
        new(
            name, ns, "Test", 10, 1, 0, 0, 0, 0, 0, 0, codeHealth,
            [],
            FilePath: $"{ns}/{name}.cs",
            StartLine: 1,
            QualifiedName: $"{ns}.{name}",
            TypeId: $"Test::{ns}.{name}");

    static bool IsSortedAscending<T>(IReadOnlyList<T> items, Func<T, double> keySelector)
    {
        for (var i = 1; i < items.Count; i++)
        {
            if (keySelector(items[i - 1]) > keySelector(items[i]))
                return false;
        }
        return true;
    }
}
