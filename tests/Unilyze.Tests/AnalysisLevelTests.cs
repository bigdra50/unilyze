using System.Text.Json;

namespace Unilyze.Tests;

public sealed class AnalysisLevelTests
{
    [Theory]
    [InlineData("syntax", AnalysisLevel.SyntaxOnly)]
    [InlineData("core", AnalysisLevel.CoreEngine)]
    [InlineData("full", AnalysisLevel.FullEngine)]
    [InlineData("complete", AnalysisLevel.Complete)]
    [InlineData("COMPLETE", AnalysisLevel.Complete)]
    [InlineData("  core  ", AnalysisLevel.CoreEngine)]
    public void TryParse_KnownTokens_ReturnsLevel(string value, AnalysisLevel expected)
    {
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
