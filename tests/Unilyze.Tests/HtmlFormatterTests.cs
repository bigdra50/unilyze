using System.Text.Json;

namespace Unilyze.Tests;

public sealed class HtmlFormatterTests
{
    [Fact]
    public void Generate_EmbedsOfflineFallbackReport()
    {
        var result = MakeEmptyResult();

        var json = JsonSerializer.Serialize(result, AnalysisJsonContext.Default.AnalysisResult);

        var html = HtmlFormatter.Generate(json, result.ProjectPath);

        Assert.Contains("renderOfflineReport()", html);
        Assert.Contains("Offline report view", html);
    }

    [Fact]
    public void Generate_LeavesDiffPlaceholderAsNull()
    {
        var result = MakeEmptyResult();
        var json = JsonSerializer.Serialize(result, AnalysisJsonContext.Default.AnalysisResult);

        var html = HtmlFormatter.Generate(json, result.ProjectPath);

        Assert.DoesNotContain("__DIFF_DATA_PLACEHOLDER__", html);
        Assert.Contains("const DIFF = null;", html);
    }

    [Fact]
    public void Generate_BundlesVendorScriptsOffline()
    {
        var result = MakeEmptyResult();
        var json = JsonSerializer.Serialize(result, AnalysisJsonContext.Default.AnalysisResult);

        var html = HtmlFormatter.Generate(json, result.ProjectPath);

        Assert.DoesNotContain("__VENDOR_SCRIPTS__", html);
        Assert.Contains("The Cytoscape Consortium", html);
        Assert.Contains("<!-- Cytoscape.js 3.30.4", html);
        Assert.Contains("<!-- dagre 0.8.5", html);
        Assert.Contains("<!-- cytoscape-dagre 2.5.0", html);
        Assert.DoesNotContain("unpkg.com/cytoscape@", html);
        Assert.DoesNotContain("unpkg.com/dagre@", html);
        Assert.DoesNotContain("unpkg.com/cytoscape-dagre@", html);
        Assert.Contains("unpkg.com/elkjs@", html);
        Assert.Contains("unpkg.com/cytoscape-elk@", html);
    }

    [Fact]
    public void GenerateWithDiff_EmbedsDiffData()
    {
        var before = MakeResultWithType(codeHealth: 8.0, maxCogCC: 5);
        var after = MakeResultWithType(codeHealth: 6.0, maxCogCC: 12);

        var diff = DiffCalculator.Compare(before, after);
        var afterJson = JsonSerializer.Serialize(after, AnalysisJsonContext.Default.AnalysisResult);
        var diffJson = JsonSerializer.Serialize(diff, AnalysisJsonContext.Default.DiffResult);

        var html = HtmlFormatter.GenerateWithDiff(afterJson, diffJson, after.ProjectPath);

        Assert.DoesNotContain("__DIFF_DATA_PLACEHOLDER__", html);
        Assert.Contains("\"degraded\":", html);
        Assert.Contains("\"summary\":", html);
        Assert.Contains("diffSum", html);
        // graph-mode diff wiring (#73): bucket data attribute and halo styling
        Assert.Contains("diffBucket", html);
        Assert.Contains("underlay-color", html);
    }

    static AnalysisResult MakeEmptyResult() => new(
        "/tmp/SampleProject",
        DateTimeOffset.UtcNow,
        [], [], []);

    static AnalysisResult MakeResultWithType(double codeHealth, int maxCogCC) => new(
        "/tmp/SampleProject",
        DateTimeOffset.UtcNow,
        Assemblies: [],
        Types: [],
        Dependencies: [],
        TypeMetrics: [
            new TypeMetrics(
                TypeName: "Foo",
                Namespace: "Sample",
                Assembly: "Sample.Asm",
                LineCount: 100,
                MethodCount: 5,
                MaxNestingDepth: 2,
                AverageCognitiveComplexity: 3.0,
                MaxCognitiveComplexity: maxCogCC,
                AverageCyclomaticComplexity: 3.0,
                MaxCyclomaticComplexity: 5,
                ExcessiveParameterMethodCount: 0,
                CodeHealth: codeHealth,
                Methods: [],
                QualifiedName: "Sample.Foo",
                TypeId: "Sample.Asm::Sample.Foo")
        ]);
}
