using System.Text.Json;

namespace Unilyze.Tests;

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

    // --- ParseGitLog ---

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
    public void ParseGitLog_EmptyOutput()
    {
        Assert.Empty(HotspotAnalyzer.ParseGitLog(""));
        Assert.Empty(HotspotAnalyzer.ParseGitLog("   "));
        Assert.Empty(HotspotAnalyzer.ParseGitLog("\n\n\n"));
    }

    [Fact]
    public void ParseGitLog_DuplicateFiles_CountedCorrectly()
    {
        var output = "a.cs\na.cs\na.cs\nb.cs\nb.cs\n";
        var result = HotspotAnalyzer.ParseGitLog(output);

        Assert.Equal(2, result.Count);
        var a = result.Single(f => f.RelativePath == "a.cs");
        var b = result.Single(f => f.RelativePath == "b.cs");
        Assert.Equal(3, a.ChangeCount);
        Assert.Equal(2, b.ChangeCount);
    }

    [Fact]
    public void ParseGitLog_NonCSharpFiles_Included()
    {
        // git log filters by *.cs, but ParseGitLog itself doesn't filter
        var output = "readme.md\nscript.cs\n";
        var result = HotspotAnalyzer.ParseGitLog(output);

        Assert.Equal(2, result.Count);
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

        var result = HotspotAnalyzer.Analyze(types, changes, "/project", "12.month", 20);

        Assert.Single(result.Hotspots);
        Assert.Equal("TestClass", result.Hotspots[0].TypeName);
        Assert.Equal(10, result.Hotspots[0].ChangeCount);
    }

    [Fact]
    public void Analyze_ScoreCalculation()
    {
        var types = new[]
        {
            MakeTypeMetrics(filePath: "/project/src/A.cs", codeHealth: 3.0)
        };
        var changes = new[] { new FileChangeFrequency("src/A.cs", 20) };

        var result = HotspotAnalyzer.Analyze(types, changes, "/project", "12.month", 20);

        Assert.Single(result.Hotspots);
        // score = 20 * (10.0 - 3.0) = 140.0
        Assert.Equal(140.0, result.Hotspots[0].HotspotScore);
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

        var result = HotspotAnalyzer.Analyze(types, changes, "/project", "12.month", 3);

        Assert.Equal(3, result.Hotspots.Count);
        Assert.Equal(3, result.TopN);
    }

    [Fact]
    public void Analyze_NoChanges_EmptyHotspots()
    {
        var types = new[] { MakeTypeMetrics(filePath: "/project/src/A.cs") };
        var changes = Array.Empty<FileChangeFrequency>();

        var result = HotspotAnalyzer.Analyze(types, changes, "/project", "12.month", 20);

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

        var result = HotspotAnalyzer.Analyze(types, changes, "/project", "12.month", 20);

        Assert.Equal(3, result.Hotspots.Count);
        Assert.Equal("High", result.Hotspots[0].TypeName);   // 10 * (10-2) = 80
        Assert.Equal("Mid", result.Hotspots[1].TypeName);    // 10 * (10-5) = 50
        Assert.Equal("Low", result.Hotspots[2].TypeName);    // 10 * (10-9) = 10
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
                    45, 3.2, 18.5, 42, 306.0)
            ]);

        var json = JsonSerializer.Serialize(hotspot, AnalysisJsonContext.Default.HotspotResult);
        var parsed = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.HotspotResult);

        Assert.NotNull(parsed);
        Assert.Equal("/project", parsed.ProjectPath);
        Assert.Equal("12.month", parsed.Since);
        Assert.Single(parsed.Hotspots);
        Assert.Equal("PlayerService", parsed.Hotspots[0].TypeName);
        Assert.Equal(45, parsed.Hotspots[0].ChangeCount);
        Assert.Equal(306.0, parsed.Hotspots[0].HotspotScore);
    }
}
