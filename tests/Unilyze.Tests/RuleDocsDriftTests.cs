namespace Unilyze.Tests;

public class RuleDocsDriftTests
{
    const string StartMarker = "<!-- docsgen:start -->";
    const string EndMarker = "<!-- docsgen:end -->";

    [Fact]
    public void RulePages_MatchRuleMetadata()
    {
        var repositoryRoot = ResolveRepositoryRoot();

        foreach (var (ruleId, _) in SarifFormatter.EnumerateRules())
        {
            var path = Path.Combine(repositoryRoot, "docs", "rules", $"{ruleId}.md");
            Assert.True(File.Exists(path), $"Missing docs/rules/{ruleId}.md");

            var actual = Normalize(ExtractGeneratedBlock(File.ReadAllText(path), path));
            Assert.Equal(Normalize(RuleDocRenderer.Render(ruleId)), actual);
        }
    }

    [Fact]
    public void RuleIndex_ListsEveryRule()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var path = Path.Combine(repositoryRoot, "docs", "rules", "index.md");
        Assert.True(File.Exists(path), "Missing docs/rules/index.md");

        var content = Normalize(File.ReadAllText(path));
        foreach (var (ruleId, _) in SarifFormatter.EnumerateRules())
        {
            Assert.Contains($"| {ruleId} |", content, StringComparison.Ordinal);
            Assert.Contains($"({ruleId}.md)", content, StringComparison.Ordinal);
        }
    }

    static string ExtractGeneratedBlock(string content, string path)
    {
        var start = content.IndexOf(StartMarker, StringComparison.Ordinal);
        var end = content.IndexOf(EndMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing {StartMarker} in {path}");
        Assert.True(end > start, $"Missing {EndMarker} in {path}");

        return content[(start + StartMarker.Length)..end];
    }

    static string Normalize(string value) => value.Replace("\r\n", "\n");

    static string ResolveRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "mkdocs.yml")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found from test base directory.");
    }
}
