using System.Text.Json;

namespace Unilyze.Tests.History;

public class HotspotAnalyzerTests
{
    static TypeMetrics MakeTypeMetrics(
        string typeName = "TestClass",
        string ns = "TestNs",
        string assembly = "TestAssembly",
        double codeHealth = 8.0,
        double avgCogCC = 3.0,
        int maxCogCC = 5,
        string? filePath = null)
    {
        return new TypeMetrics(
            typeName, ns, assembly,
            100, 5, 2,
            avgCogCC, maxCogCC, 3.0, 5,
            0, codeHealth,
            [],
            FilePath: filePath);
    }

    static string FormatCommit(string hash, string author, string email, long timestamp, params string[] files)
    {
        var header = $"\x01{hash}\x1f{author}\x1f{email}\x1f{timestamp}";
        return files.Length == 0 ? header : header + "\n" + string.Join('\n', files);
    }

    static HotspotAnalysisContext MakeContext(
        string projectPath = "/project",
        string since = "12.month",
        int topN = 20,
        string? halfLife = null)
    {
        TimeSpan? span = null;
        if (halfLife is not null && HalfLifeParser.TryParse(halfLife, out var parsed, out _))
            span = parsed;

        return new HotspotAnalysisContext(projectPath, since, topN, true, 0, halfLife, span);
    }

    // --- ParseCommitLog ---

    [Fact]
    public void ParseCommitLog_BasicOutput()
    {
        var output = FormatCommit("abc", "Alice", "alice@example.com", 1000, "src/A.cs", "src/B.cs")
                     + "\n\n"
                     + FormatCommit("def", "Bob", "bob@example.com", 2000, "src/A.cs");

        var result = HotspotAnalyzer.ParseCommitLog(output);

        Assert.Equal(2, result.Count);
        Assert.Equal("abc", result[0].Hash);
        Assert.Equal("Alice", result[0].AuthorName);
        Assert.Equal(2, result[0].ChangedFiles.Count);
        Assert.Equal("def", result[1].Hash);
        Assert.Single(result[1].ChangedFiles);
    }

    [Fact]
    public void ParseCommitLog_EmptyOutput()
    {
        Assert.Empty(HotspotAnalyzer.ParseCommitLog(""));
        Assert.Empty(HotspotAnalyzer.ParseCommitLog("   "));
        Assert.Empty(HotspotAnalyzer.ParseCommitLog("\n\n\n"));
    }

    [Fact]
    public void ParseCommitLog_CrlfLines_ParsedCorrectly()
    {
        var output = FormatCommit("abc", "Alice", "alice@example.com", 1000, "src/A.cs", "src/B.cs")
                     .Replace("\n", "\r\n");
        var commits = HotspotAnalyzer.ParseCommitLog(output);

        Assert.Single(commits);
        Assert.Equal("abc", commits[0].Hash);
        Assert.Equal(2, commits[0].ChangedFiles.Count);
        Assert.Equal("src/A.cs", commits[0].ChangedFiles[0]);
        Assert.Equal("src/B.cs", commits[0].ChangedFiles[1]);
    }

    [Fact]
    public void ParseCommitLog_CrlfFilePaths_TrimmedAndCounted()
    {
        var output = FormatCommit("abc", "Alice", "alice@example.com", 1000, "src/A.cs\r", "src/B.cs\r\n");
        var commits = HotspotAnalyzer.ParseCommitLog(output);

        Assert.Single(commits);
        Assert.Equal(2, commits[0].ChangedFiles.Count);
        Assert.Equal("src/A.cs", commits[0].ChangedFiles[0]);
        Assert.Equal("src/B.cs", commits[0].ChangedFiles[1]);
    }

    [Fact]
    public void ParseGitLog_BasicOutput()
    {
        var output = "Assets/Scripts/Player.cs\nAssets/Scripts/Enemy.cs\nAssets/Scripts/Player.cs\n";
        var result = HotspotAnalyzer.ParseGitLog(output);

        Assert.Equal(2, result.Count);
        Assert.Equal("Assets/Scripts/Player.cs", result[0].RelativePath);
        Assert.Equal(2, result[0].ChangeCount);
        Assert.Equal("Assets/Scripts/Enemy.cs", result[1].RelativePath);
        Assert.Equal(1, result[1].ChangeCount);
    }

    [Fact]
    public void ParseGitLog_CrlfOutput()
    {
        var output = "a.cs\r\na.cs\r\na.cs\r\nb.cs\r\nb.cs\r\n";
        var result = HotspotAnalyzer.ParseGitLog(output);

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result.Single(f => f.RelativePath == "a.cs").ChangeCount);
        Assert.Equal(2, result.Single(f => f.RelativePath == "b.cs").ChangeCount);
    }

    // --- BotAuthorMatcher ---

    [Theory]
    [InlineData("dependabot[bot]", "dependabot@users.noreply.github.com", true)]
    [InlineData("renovate[bot]", "renovate@example.com", true)]
    [InlineData("github-actions[bot]", "github-actions@users.noreply.github.com", true)]
    [InlineData("Abbott", "abbott@example.com", false)]
    [InlineData("Alice", "alice@example.com", false)]
    [InlineData("dependabot", "dependabot@users.noreply.github.com", true)]
    public void BotAuthorMatcher_BuiltInPatterns(string name, string email, bool expected)
    {
        var matcher = BotAuthorMatcher.CreateDefault();
        Assert.Equal(expected, matcher.IsBot(name, email));
    }

    [Fact]
    public void BotAuthorMatcher_CustomPattern()
    {
        var matcher = BotAuthorMatcher.CreateDefault();
        matcher.AddPattern("^ci-.*");
        Assert.True(matcher.IsBot("ci-runner", "ci@example.com"));
        Assert.False(matcher.IsBot("city-planner", "city@example.com"));
    }

    [Fact]
    public void BotAuthorMatcher_InvalidRegex_Throws()
    {
        var matcher = BotAuthorMatcher.CreateDefault();
        var ex = Assert.Throws<InvalidOperationException>(() => matcher.AddPattern("["));
        Assert.Contains("Invalid bot pattern", ex.Message);
    }

    // --- Aggregate / decay ---

    [Fact]
    public void ApplyBotFilter_ExcludesBots()
    {
        var commits = new[]
        {
            new GitCommitRecord("a", "Alice", "alice@example.com", 1000, ["src/A.cs"]),
            new GitCommitRecord("b", "dependabot[bot]", "dependabot@users.noreply.github.com", 2000, ["src/B.cs"]),
        };
        var matcher = BotAuthorMatcher.CreateDefault();
        var (included, excluded) = HotspotAnalyzer.ApplyBotFilter(commits, matcher, botFilterEnabled: true);

        Assert.Single(included);
        Assert.Equal("a", included[0].Hash);
        Assert.Equal(1, excluded);
    }

    [Fact]
    public void AggregateFileChanges_DecayWeightsRecentHigher()
    {
        var commits = new[]
        {
            new GitCommitRecord("old", "Alice", "alice@example.com", 1000, ["src/A.cs"]),
            new GitCommitRecord("new", "Alice", "alice@example.com", 2000, ["src/A.cs"]),
        };
        var halfLife = TimeSpan.FromDays(90);
        var result = HotspotAnalyzer.AggregateFileChanges(commits, halfLife);

        Assert.Single(result);
        Assert.Equal(2, result[0].ChangeCount);
        Assert.True(result[0].WeightedChurn < 2.0);
        Assert.True(result[0].WeightedChurn > 1.0);
    }

    [Fact]
    public void ComputeDecayWeight_AnchoredAtNewest()
    {
        var anchor = 2000L;
        var recent = HotspotAnalyzer.ComputeDecayWeight(2000, anchor, TimeSpan.FromDays(90));
        var older = HotspotAnalyzer.ComputeDecayWeight(1000, anchor, TimeSpan.FromDays(90));

        Assert.Equal(1.0, recent, 3);
        Assert.True(older < 1.0);
    }

    // --- Analyze ---

    [Fact]
    public void Analyze_MatchesFileToType()
    {
        var types = new[]
        {
            MakeTypeMetrics(filePath: "/project/Assets/Scripts/Player.cs", codeHealth: 5.0)
        };
        var changes = new[] { new FileChangeFrequency("Assets/Scripts/Player.cs", 10) };

        var result = HotspotAnalyzer.Analyze(types, changes, MakeContext());

        Assert.Single(result.Hotspots);
        Assert.Equal("TestClass", result.Hotspots[0].TypeName);
        Assert.Equal(10, result.Hotspots[0].ChangeCount);
        Assert.True(result.BotFilter);
    }

    [Fact]
    public void Analyze_ScoreCalculation()
    {
        var types = new[]
        {
            MakeTypeMetrics(filePath: "/project/src/A.cs", codeHealth: 3.0)
        };
        var changes = new[] { new FileChangeFrequency("src/A.cs", 20) };

        var result = HotspotAnalyzer.Analyze(types, changes, MakeContext());

        Assert.Single(result.Hotspots);
        Assert.Equal(140.0, result.Hotspots[0].HotspotScore);
    }

    [Fact]
    public void Analyze_WithDecay_UsesWeightedChurn()
    {
        var types = new[]
        {
            MakeTypeMetrics(filePath: "/project/src/A.cs", codeHealth: 5.0)
        };
        var changes = new[] { new FileChangeFrequency("src/A.cs", 10, WeightedChurn: 6.5) };

        var result = HotspotAnalyzer.Analyze(types, changes, MakeContext(halfLife: "90.day"));

        Assert.Single(result.Hotspots);
        Assert.Equal(6.5, result.Hotspots[0].WeightedChurn);
        Assert.Equal(32.5, result.Hotspots[0].HotspotScore);
    }

    [Fact]
    public void Analyze_TopN_LimitsResults()
    {
        var types = Enumerable.Range(0, 10)
            .Select(i => MakeTypeMetrics(
                typeName: $"Type{i}",
                filePath: $"/project/src/Type{i}.cs",
                codeHealth: 5.0))
            .ToList();
        var changes = Enumerable.Range(0, 10)
            .Select(i => new FileChangeFrequency($"src/Type{i}.cs", 10 - i))
            .ToList();

        var result = HotspotAnalyzer.Analyze(types, changes, MakeContext(topN: 3));

        Assert.Equal(3, result.Hotspots.Count);
        Assert.Equal(3, result.TopN);
    }

    [Fact]
    public void Analyze_NoChanges_EmptyHotspots()
    {
        var types = new[] { MakeTypeMetrics(filePath: "/project/src/A.cs") };
        var changes = Array.Empty<FileChangeFrequency>();

        var result = HotspotAnalyzer.Analyze(types, changes, MakeContext());

        Assert.Empty(result.Hotspots);
    }

    [Fact]
    public void Analyze_SortedByScore_Descending()
    {
        var types = new[]
        {
            MakeTypeMetrics(typeName: "Low", filePath: "/project/src/Low.cs", codeHealth: 9.0),
            MakeTypeMetrics(typeName: "High", filePath: "/project/src/High.cs", codeHealth: 2.0),
            MakeTypeMetrics(typeName: "Mid", filePath: "/project/src/Mid.cs", codeHealth: 5.0),
        };
        var changes = new[]
        {
            new FileChangeFrequency("src/Low.cs", 10),
            new FileChangeFrequency("src/High.cs", 10),
            new FileChangeFrequency("src/Mid.cs", 10),
        };

        var result = HotspotAnalyzer.Analyze(types, changes, MakeContext());

        Assert.Equal(3, result.Hotspots.Count);
        Assert.Equal("High", result.Hotspots[0].TypeName);
        Assert.Equal("Mid", result.Hotspots[1].TypeName);
        Assert.Equal("Low", result.Hotspots[2].TypeName);
    }

    // --- HalfLifeParser ---

    [Theory]
    [InlineData("90.day", true)]
    [InlineData("6.month", true)]
    [InlineData("1.year", true)]
    [InlineData("bogus", false)]
    [InlineData("90", false)]
    public void HalfLifeParser_Parse(string value, bool expectedValid)
    {
        var valid = HalfLifeParser.TryParse(value, out var span, out _);
        Assert.Equal(expectedValid, valid);
        if (expectedValid)
            Assert.True(span > TimeSpan.Zero);
    }

    // --- JSON Serialization ---

    [Fact]
    public void JsonSerialization_HotspotResult()
    {
        var hotspot = new HotspotResult(
            "/project",
            "12.month",
            20,
            [
                new TypeHotspot(
                    "PlayerService", "App.Domain", "App.Domain",
                    "Assets/Scripts/Domain/PlayerService.cs",
                    45, null, 3.2, 18.5, 42, 306.0)
            ],
            BotFilter: true,
            BotCommitsExcluded: 3,
            HalfLife: "90.day",
            MethodHotspots:
            [
                new MethodHotspot("Run", "PlayerService", "App.Domain", 10, 50, 12, 8.5, 15, 127.5)
            ]);

        var json = JsonSerializer.Serialize(hotspot, AnalysisJsonContext.Default.HotspotResult);
        var parsed = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.HotspotResult);

        Assert.NotNull(parsed);
        Assert.Equal("/project", parsed.ProjectPath);
        Assert.True(parsed.BotFilter);
        Assert.Equal(3, parsed.BotCommitsExcluded);
        Assert.Equal("90.day", parsed.HalfLife);
        Assert.Single(parsed.Hotspots);
        Assert.Single(parsed.MethodHotspots!);
        Assert.Equal("Run", parsed.MethodHotspots![0].MethodName);
    }
}
