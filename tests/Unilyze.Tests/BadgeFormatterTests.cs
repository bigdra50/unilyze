using System.Text.Json;

namespace Unilyze.Tests;

public sealed class BadgeFormatterTests
{
    [Fact]
    public void Build_CodeHealth_HighMin_ReturnsBrightgreen()
    {
        var summary = new StatuslineFormatter.Summary(9.2, 8.5, 0, 0, 10, 80, 0, 0);
        var badge = BadgeFormatter.Build(BadgeMetric.CodeHealth, summary);

        Assert.Equal(1, badge.SchemaVersion);
        Assert.Equal("code health", badge.Label);
        Assert.Equal("9.2 / 8.5", badge.Message);
        Assert.Equal("brightgreen", badge.Color);
    }

    [Fact]
    public void Build_CodeHealth_MediumMin_ReturnsYellow()
    {
        var summary = new StatuslineFormatter.Summary(9.2, 6.1, 0, 0, 10, 80, 0, 0);
        var badge = BadgeFormatter.Build(BadgeMetric.CodeHealth, summary);

        Assert.Equal("yellow", badge.Color);
    }

    [Fact]
    public void Build_CodeHealth_LowMin_ReturnsRed()
    {
        var summary = new StatuslineFormatter.Summary(8.0, 4.8, 0, 0, 10, 80, 0, 0);
        var badge = BadgeFormatter.Build(BadgeMetric.CodeHealth, summary);

        Assert.Equal("red", badge.Color);
    }

    [Theory]
    [InlineData(74.6, "75", "yellow")]
    [InlineData(82.0, "82", "brightgreen")]
    [InlineData(55.0, "55", "red")]
    public void Build_Mi_ReturnsExpectedMessageAndColor(double mi, string expectedMessage, string expectedColor)
    {
        var summary = new StatuslineFormatter.Summary(9.0, 9.0, 0, 0, 10, mi, 0, 0);
        var badge = BadgeFormatter.Build(BadgeMetric.Mi, summary);

        Assert.Equal("maintainability", badge.Label);
        Assert.Equal(expectedMessage, badge.Message);
        Assert.Equal(expectedColor, badge.Color);
    }

    [Fact]
    public void Build_Smells_NoWarnings_ReturnsBrightgreen()
    {
        var summary = new StatuslineFormatter.Summary(9.0, 9.0, 0, 0, 10, 80, 0, 0);
        var badge = BadgeFormatter.Build(BadgeMetric.Smells, summary);

        Assert.Equal("smells", badge.Label);
        Assert.Equal("0", badge.Message);
        Assert.Equal("brightgreen", badge.Color);
    }

    [Fact]
    public void Build_Smells_WarningsOnly_ReturnsYellow()
    {
        var summary = new StatuslineFormatter.Summary(9.0, 9.0, 12, 0, 10, 80, 0, 0);
        var badge = BadgeFormatter.Build(BadgeMetric.Smells, summary);

        Assert.Equal("12", badge.Message);
        Assert.Equal("yellow", badge.Color);
    }

    [Fact]
    public void Build_Smells_Criticals_ReturnsRed()
    {
        var summary = new StatuslineFormatter.Summary(9.0, 9.0, 12, 3, 10, 80, 0, 0);
        var badge = BadgeFormatter.Build(BadgeMetric.Smells, summary);

        Assert.Equal("12", badge.Message);
        Assert.Equal("red", badge.Color);
    }

    [Theory]
    [InlineData("codehealth", "code health")]
    [InlineData("mi", "maintainability")]
    [InlineData("smells", "smells")]
    public void Build_ZeroTypes_ReturnsNaLightgrey(string metricStr, string expectedLabel)
    {
        BadgeFormatter.TryParseMetric(metricStr, out var metric);
        var summary = new StatuslineFormatter.Summary(0.0, 0.0, 0, 0, 0, 0.0, 0, 0);
        var badge = BadgeFormatter.Build(metric, summary);

        Assert.Equal(expectedLabel, badge.Label);
        Assert.Equal("n/a", badge.Message);
        Assert.Equal("lightgrey", badge.Color);
    }

    [Fact]
    public void Serialize_ProducesCompactCamelCaseJson()
    {
        var badge = BadgeFormatter.Build(
            BadgeMetric.CodeHealth,
            new StatuslineFormatter.Summary(9.2, 8.5, 0, 0, 10, 80, 0, 0));

        var json = BadgeFormatter.Serialize(badge);

        Assert.DoesNotContain('\n', json);

        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(JsonValueKind.Number, root.GetProperty("schemaVersion").ValueKind);
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(JsonValueKind.String, root.GetProperty("label").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("message").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("color").ValueKind);
    }

    [Theory]
    [InlineData("CodeHealth", true, "codehealth")]
    [InlineData("mi", true, "mi")]
    [InlineData("SMELLS", true, "smells")]
    [InlineData("", true, "codehealth")]
    [InlineData("bogus", false, "codehealth")]
    public void TryParseMetric_ParsesKnownValues(string? value, bool expected, string expectedMetricStr)
    {
        var result = BadgeFormatter.TryParseMetric(value, out var metric);

        Assert.Equal(expected, result);
        BadgeFormatter.TryParseMetric(expectedMetricStr, out var expectedMetric);
        Assert.Equal(expectedMetric, metric);
    }

    [Fact]
    public void Build_PopulatesAnalysisLevel_FromSummary()
    {
        var summary = new StatuslineFormatter.Summary(9.2, 8.5, 0, 0, 10, 80, 0, 0, AnalysisLevel: "SyntaxOnly");
        var badge = BadgeFormatter.Build(BadgeMetric.CodeHealth, summary);

        Assert.Equal("SyntaxOnly", badge.AnalysisLevel);
    }

    [Fact]
    public void Build_ZeroTypes_PopulatesAnalysisLevel()
    {
        var summary = new StatuslineFormatter.Summary(0.0, 0.0, 0, 0, 0, 0.0, 0, 0, AnalysisLevel: "CoreEngine");
        var badge = BadgeFormatter.Build(BadgeMetric.CodeHealth, summary);

        Assert.Equal("n/a", badge.Message);
        Assert.Equal("CoreEngine", badge.AnalysisLevel);
    }

    [Fact]
    public void Serialize_IncludesAnalysisLevelField_WhenSet()
    {
        var summary = new StatuslineFormatter.Summary(9.2, 8.5, 0, 0, 10, 80, 0, 0, AnalysisLevel: "FullEngine");
        var json = BadgeFormatter.Serialize(BadgeFormatter.Build(BadgeMetric.CodeHealth, summary));

        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal("FullEngine", root.GetProperty("analysisLevel").GetString());
    }

    [Fact]
    public void Serialize_OmitsAnalysisLevelField_WhenNull()
    {
        var summary = new StatuslineFormatter.Summary(9.2, 8.5, 0, 0, 10, 80, 0, 0);
        var json = BadgeFormatter.Serialize(BadgeFormatter.Build(BadgeMetric.CodeHealth, summary));

        var root = JsonDocument.Parse(json).RootElement;
        Assert.False(root.TryGetProperty("analysisLevel", out _));
    }

    [Fact]
    public void TryParseMetric_Null_ReturnsCodeHealth()
    {
        var result = BadgeFormatter.TryParseMetric(null, out var metric);

        Assert.True(result);
        Assert.Equal(BadgeMetric.CodeHealth, metric);
    }

    [Theory]
    [InlineData(null, true, BadgeFormat.Json)]
    [InlineData("", true, BadgeFormat.Json)]
    [InlineData("json", true, BadgeFormat.Json)]
    [InlineData("SVG", true, BadgeFormat.Svg)]
    [InlineData("bogus", false, BadgeFormat.Json)]
    internal void TryParseFormat_ParsesKnownValues(string? value, bool expectedResult, BadgeFormat expectedFormat)
    {
        var result = BadgeFormatter.TryParseFormat(value, out var format);

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedFormat, format);
    }
}
