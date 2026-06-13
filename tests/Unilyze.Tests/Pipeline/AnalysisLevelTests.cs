using System.Text.Json;

namespace Unilyze.Tests.Pipeline;

public sealed class AnalysisLevelTests
{
    [Theory]
    [InlineData("syntax", nameof(AnalysisLevel.Syntax))]
    [InlineData("core", nameof(AnalysisLevel.Core))]
    [InlineData("full", nameof(AnalysisLevel.Full))]
    [InlineData("complete", nameof(AnalysisLevel.Complete))]
    [InlineData("COMPLETE", nameof(AnalysisLevel.Complete))]
    [InlineData("  core  ", nameof(AnalysisLevel.Core))]
    public void TryParse_KnownTokens_ReturnsLevel(string value, string expectedName)
    {
        var expected = Enum.Parse<AnalysisLevel>(expectedName);

        Assert.True(AnalysisLevelOption.TryParse(value, out var level));
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("semantic")]
    [InlineData("bogus")]
    public void TryParse_UnknownTokens_ReturnsFalse(string? value)
    {
        Assert.False(AnalysisLevelOption.TryParse(value, out _));
    }

    [Theory]
    [InlineData("SyntaxOnly", "[syntax]")]
    [InlineData("CoreEngine", "[core]")]
    [InlineData("FullEngine", "[full]")]
    public void StatuslineMarker_BelowComplete_ReturnsMarker(string level, string expected)
    {
        Assert.Equal(expected, AnalysisLevelOption.StatuslineMarker(level));
    }

    [Theory]
    [InlineData("Complete")]
    [InlineData(null)]
    [InlineData("unknown")]
    public void StatuslineMarker_CompleteOrUnknown_ReturnsNull(string? level)
    {
        Assert.Null(AnalysisLevelOption.StatuslineMarker(level));
    }

    [Theory]
    [InlineData(nameof(AnalysisLevel.Syntax), "SyntaxOnly")]
    [InlineData(nameof(AnalysisLevel.Core), "CoreEngine")]
    [InlineData(nameof(AnalysisLevel.Full), "FullEngine")]
    [InlineData(nameof(AnalysisLevel.Complete), "Complete")]
    public void ToExternalName_MapsEnumToLegacyStrings(string levelName, string expected)
    {
        var level = Enum.Parse<AnalysisLevel>(levelName);

        Assert.Equal(expected, AnalysisLevelOption.ToExternalName(level));
    }

    [Theory]
    [InlineData(nameof(AnalysisLevel.Syntax), "SyntaxOnly")]
    [InlineData(nameof(AnalysisLevel.Core), "Complete")]
    [InlineData(nameof(AnalysisLevel.Full), "Complete")]
    [InlineData(nameof(AnalysisLevel.Complete), "Complete")]
    public void ToExternalName_DotnetProject_NeverUsesEngineFlavoredLevels(string levelName, string expected)
    {
        var level = Enum.Parse<AnalysisLevel>(levelName);

        Assert.Equal(expected, AnalysisLevelOption.ToExternalName(level, "dotnet"));
    }

    [Fact]
    public void AnalysisResult_RoundTrips_AnalysisLevel()
    {
        var result = new AnalysisResult(
            "/test", DateTimeOffset.UtcNow, [], [], [],
            TypeMetrics: null, AnalysisLevel: "CoreEngine");

        var json = JsonSerializer.Serialize(result, AnalysisJsonContext.Default.AnalysisResult);
        Assert.Contains("\"analysisLevel\": \"CoreEngine\"", json);

        var restored = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.AnalysisResult);
        Assert.NotNull(restored);
        Assert.Equal("CoreEngine", restored!.AnalysisLevel);
    }

    [Fact]
    public void AnalysisResult_LegacyJsonWithoutLevel_DeserializesWithNullLevel()
    {
        // Older snapshots predate the analysisLevel field; deserialization must not fail.
        const string legacyJson = """
            {
              "projectPath": "/legacy",
              "analyzedAt": "2024-01-01T00:00:00+00:00",
              "assemblies": [],
              "types": [],
              "dependencies": []
            }
            """;

        var restored = JsonSerializer.Deserialize(legacyJson, AnalysisJsonContext.Default.AnalysisResult);

        Assert.NotNull(restored);
        Assert.Null(restored!.AnalysisLevel);
        Assert.Equal("/legacy", restored.ProjectPath);
    }
}
