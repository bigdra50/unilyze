using System.Text.Json;

namespace Unilyze.Tests;

public sealed class McpToolHandlerTests
{
    static readonly string GoldenFixturePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "golden", "expected.json"));

    static McpToolArgs Args(params (string Key, object Value)[] pairs)
    {
        var obj = new Dictionary<string, object> { ["input"] = GoldenFixturePath };
        foreach (var (key, value) in pairs)
            obj[key] = value;
        var json = JsonSerializer.Serialize(obj);
        return McpToolArgs.From(JsonSerializer.Deserialize<JsonElement>(json));
    }

    [Fact]
    public void GetSummary_FromFixture_ContainsVersionsAndNoTypeMetricsDump()
    {
        var handlers = new McpToolHandlers();
        var result = handlers.Call("get_summary", Args());

        Assert.False(result.IsError);
        Assert.Contains("metricsVersion:", result.Text);
        Assert.Contains("toolVersion:", result.Text);
        Assert.DoesNotContain("typeMetrics", result.Text);
    }

    [Fact]
    public void WorstTypes_MarkdownMatchesQueryPipeline()
    {
        var handlers = new McpToolHandlers();
        var mcpResult = handlers.Call("worst_types", Args(("count", 5)));

        var json = File.ReadAllText(GoldenFixturePath);
        var analysis = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.AnalysisResult)!;
        var selection = QuerySelector.SelectWorst(analysis.TypeMetrics!, 5);
        var queryResult = QueryEvidenceAssembler.Build(analysis, selection.Types);
        var expected = QueryEvidenceFormatter.ToMarkdown(queryResult);

        Assert.False(mcpResult.IsError);
        Assert.Equal(expected, mcpResult.Text);
    }

    [Fact]
    public void QueryType_AmbiguousName_ReturnsCandidates()
    {
        var handlers = new McpToolHandlers();
        var metrics = new List<TypeMetrics>
        {
            CreateType("Foo", "Alpha", 5.0),
            CreateType("Foo", "Beta", 6.0),
        };
        var analysis = LoadGoldenAnalysis() with { TypeMetrics = metrics };
        WriteTempSnapshot(analysis, out var path);

        var result = handlers.Call("query_type", Args(("type", "Foo"), ("input", path)));

        Assert.False(result.IsError);
        Assert.Contains("Ambiguous type name 'Foo'", result.Text);
        Assert.Contains("Alpha.Foo", result.Text);
    }

    [Fact]
    public void BaselineStatus_Missing_ReturnsStructuredNoBaseline()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-mcp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var handlers = new McpToolHandlers();
            var argsJson = JsonSerializer.Serialize(new { path = tempDir });
            var args = McpToolArgs.From(JsonSerializer.Deserialize<JsonElement>(argsJson));
            var result = handlers.Call("baseline_status", args);

            Assert.False(result.IsError);
            using var doc = JsonDocument.Parse(result.Text);
            Assert.False(doc.RootElement.GetProperty("present").GetBoolean());
            Assert.Contains("No baseline", doc.RootElement.GetProperty("message").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Schema_ReturnsEmbeddedReference()
    {
        var handlers = new McpToolHandlers();
        var result = handlers.Call("schema", McpToolArgs.From(null));

        Assert.False(result.IsError);
        Assert.Equal(EmbeddedCliText.Schema, result.Text);
    }

    [Fact]
    public void Version_ReturnsToolAndMetricsVersion()
    {
        var handlers = new McpToolHandlers();
        var result = handlers.Call("version", McpToolArgs.From(null));

        Assert.False(result.IsError);
        using var doc = JsonDocument.Parse(result.Text);
        Assert.Equal(ToolVersionInfo.Current, doc.RootElement.GetProperty("toolVersion").GetString());
        Assert.Equal(AnalysisResult.CurrentMetricsVersion, doc.RootElement.GetProperty("metricsVersion").GetInt32());
    }

    [Fact]
    public void ResponseTrimmer_TruncatesLongText()
    {
        var text = new string('x', 100);
        var trimmed = McpResponseTrimmer.Apply(text, 40);

        Assert.True(trimmed.Length <= 40);
        Assert.Contains("truncated", trimmed);
    }

    static AnalysisResult LoadGoldenAnalysis()
    {
        var json = File.ReadAllText(GoldenFixturePath);
        return JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.AnalysisResult)!;
    }

    static void WriteTempSnapshot(AnalysisResult analysis, out string path)
    {
        path = Path.Combine(Path.GetTempPath(), $"unilyze-mcp-snap-{Guid.NewGuid():N}.json");
        var json = JsonSerializer.Serialize(analysis, AnalysisJsonContext.Default.AnalysisResult);
        File.WriteAllText(path, json);
    }

    static TypeMetrics CreateType(string name, string ns, double codeHealth) =>
        new(
            name, ns, "Test", 10, 1, 0, 0, 0, 0, 0, 0, codeHealth,
            [],
            FilePath: $"{ns}/{name}.cs",
            StartLine: 1,
            QualifiedName: $"{ns}.{name}",
            TypeId: $"Test::{ns}.{name}");
}
