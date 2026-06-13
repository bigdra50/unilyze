namespace Unilyze.Tests.Config;

public class SmellThresholdsDriftTests
{
    const string StartMarker = "<!-- smell-thresholds:start -->";
    const string EndMarker = "<!-- smell-thresholds:end -->";

    [Fact]
    public void DocsThresholdTable_MatchesSmellThresholdsRegistry()
    {
        var docsPath = ResolveDocsMetricsPath();
        var docsContent = File.ReadAllText(docsPath);
        var start = docsContent.IndexOf(StartMarker, StringComparison.Ordinal);
        var end = docsContent.IndexOf(EndMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing marker {StartMarker} in docs/metrics.md");
        Assert.True(end > start, $"Missing marker {EndMarker} in docs/metrics.md");

        var tableStart = start + StartMarker.Length;
        var actualTable = docsContent[tableStart..end].Trim();
        var expectedTable = SmellThresholds.RenderDocsThresholdTable();

        Assert.Equal(expectedTable, actualTable);
    }

    static string ResolveDocsMetricsPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "docs", "metrics.md");
            if (File.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("docs/metrics.md not found from test base directory.");
    }
}
