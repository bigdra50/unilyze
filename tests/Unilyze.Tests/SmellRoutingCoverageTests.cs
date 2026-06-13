namespace Unilyze.Tests;

public class SmellRoutingCoverageTests
{
    const string RoutingHeading = "### Detection responsibility routing";

    [Fact]
    public void RoutingTable_CoversAllCodeSmellKinds()
    {
        var docsPath = ResolveDocsMetricsPath();
        var docsContent = File.ReadAllText(docsPath);
        var sectionText = ExtractRoutingSection(docsContent);

        var missing = Enum.GetNames(typeof(CodeSmellKind))
            .Where(kind => !sectionText.Contains(kind, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"docs/metrics.md routing table is missing CodeSmellKind values: {string.Join(", ", missing)}");
    }

    static string ExtractRoutingSection(string docsContent)
    {
        var start = docsContent.IndexOf(RoutingHeading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing heading {RoutingHeading} in docs/metrics.md");

        var searchFrom = start + RoutingHeading.Length;
        var nextSection = FindNextSectionHeading(docsContent, searchFrom);
        return nextSection >= 0
            ? docsContent[start..nextSection]
            : docsContent[start..];
    }

    static int FindNextSectionHeading(string content, int searchFrom)
    {
        for (var i = searchFrom; i < content.Length; i++)
        {
            if (content[i] != '#')
                continue;
            if (i > 0 && content[i - 1] != '\n')
                continue;

            var level = 0;
            while (i + level < content.Length && content[i + level] == '#')
                level++;

            if (level is 2 or 3)
                return i;
        }

        return -1;
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
