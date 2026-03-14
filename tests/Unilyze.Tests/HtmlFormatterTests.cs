using System.Text.Json;

namespace Unilyze.Tests;

public sealed class HtmlFormatterTests
{
    [Fact]
    public void Generate_EmbedsOfflineFallbackReport()
    {
        var result = new AnalysisResult(
            "/tmp/SampleProject",
            DateTimeOffset.UtcNow,
            [],
            [],
            []);

        var json = JsonSerializer.Serialize(result, AnalysisJsonContext.Default.AnalysisResult);

        var html = HtmlFormatter.Generate(json, result.ProjectPath);

        Assert.Contains("renderOfflineReport()", html);
        Assert.Contains("Offline report view", html);
    }
}
