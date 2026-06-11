using System.Text.Json;

namespace Unilyze.Tests;

public sealed class TrendHtmlFormatterTests
{
    [Fact]
    public void Generate_ContainsInlineSvg_NoExternalRefs()
    {
        var trend = MakeTrend();
        var json = JsonSerializer.Serialize(trend, AnalysisJsonContext.Default.TrendResult);

        var html = TrendHtmlFormatter.Generate(json, "/tmp/history");

        Assert.Contains("<svg", html);
        Assert.DoesNotContain("<script src=", html);
        Assert.DoesNotContain("<link ", html);
        Assert.DoesNotContain("unpkg.com", html);
    }

    [Fact]
    public void Generate_EmbedsTrendData()
    {
        var trend = MakeTrend();
        var json = JsonSerializer.Serialize(trend, AnalysisJsonContext.Default.TrendResult);

        var html = TrendHtmlFormatter.Generate(json, "/tmp/history");

        Assert.Contains("snapshotCount", html);
        Assert.Contains("snap-a.json", html);
        Assert.Contains("snap-b.json", html);
        Assert.DoesNotContain("__TREND_DATA_PLACEHOLDER__", html);
    }

    [Fact]
    public void Generate_EscapesScriptBreakoutInPayload()
    {
        var trend = new TrendResult(
            [
                new TrendSnapshot(
                    DateTimeOffset.UtcNow,
                    "/project</script><script>alert(1)",
                    1, 8.0, 7.0, 0, 0, 1.0,
                    "evil</script><script>alert(1).json"),
            ],
            new TrendSummary(1, 0.0, 0));

        var json = JsonSerializer.Serialize(trend, AnalysisJsonContext.Default.TrendResult);
        var html = TrendHtmlFormatter.Generate(json, "/tmp/history");

        Assert.DoesNotContain("</script><script>alert(1)", html);
        Assert.Equal(html.IndexOf("</script>", StringComparison.Ordinal), html.LastIndexOf("</script>", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_RendersVersionCrossingAnnotation()
    {
        var trend = new TrendResult(
            [
                new TrendSnapshot(
                    new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    "/project", 10, 8.0, 7.0, 2, 1, 2.0,
                    "a.json", MetricsVersion: 2),
                new TrendSnapshot(
                    new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero),
                    "/project", 10, 8.5, 7.5, 1, 0, 1.5,
                    "b.json", MetricsVersion: 3),
            ],
            new TrendSummary(2, 0.5, -1));

        var json = JsonSerializer.Serialize(trend, AnalysisJsonContext.Default.TrendResult);
        var html = TrendHtmlFormatter.Generate(json, "/tmp/history");

        Assert.Contains("metrics versions differ across snapshots", html);
        Assert.Contains("stroke-dasharray=\"4 3\"", html);
        Assert.Contains("metricsVersion", html);
    }

    static TrendResult MakeTrend() => new(
        [
            new TrendSnapshot(
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                "/project", 10, 7.5, 6.0, 5, 2, 4.0,
                "snap-a.json", MetricsVersion: 3, Profile: "default",
                WarningSmellCount: 3, CriticalSmellCount: 2),
            new TrendSnapshot(
                new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero),
                "/project", 12, 8.0, 7.0, 3, 1, 3.5,
                "snap-b.json", MetricsVersion: 3, Profile: "default",
                WarningSmellCount: 2, CriticalSmellCount: 1),
        ],
        new TrendSummary(2, 0.5, -2));
}
