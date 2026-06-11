using System.Net;
using System.Text.Json;

namespace Unilyze;

public static class TrendHtmlFormatter
{
    static readonly string Template = LoadTemplate();

    public static string Generate(string trendJson, string inputDir, string? title = null)
    {
        var pageTitle = title;
        if (string.IsNullOrWhiteSpace(pageTitle))
        {
            pageTitle = Path.GetFileName(inputDir.TrimEnd('/', '\\'));
            if (string.IsNullOrEmpty(pageTitle))
                pageTitle = "Trend";
        }

        var trend = JsonSerializer.Deserialize(trendJson, AnalysisJsonContext.Default.TrendResult)
            ?? throw new InvalidOperationException("Failed to parse trend JSON.");
        var charts = TrendChartRenderer.RenderAll(trend.Snapshots);
        var warnings = BuildWarnings(trend.Snapshots);
        var safeJson = trendJson.Replace("</script", "<\\/script", StringComparison.Ordinal);

        return Template
            .Replace("__TREND_DATA_PLACEHOLDER__", safeJson)
            .Replace("__INPUT_DIR_PLACEHOLDER__", WebUtility.HtmlEncode(inputDir))
            .Replace("__TITLE__", WebUtility.HtmlEncode(pageTitle))
            .Replace("__HEALTH_CHART_PLACEHOLDER__", charts.Health)
            .Replace("__SMELLS_CHART_PLACEHOLDER__", charts.Smells)
            .Replace("__TYPES_CHART_PLACEHOLDER__", charts.Types)
            .Replace("__WARNINGS_PLACEHOLDER__", WebUtility.HtmlEncode(warnings));
    }

    static string BuildWarnings(IReadOnlyList<TrendSnapshot> snapshots)
    {
        var msgs = new List<string>();
        var versions = snapshots.Select(s => s.MetricsVersion).Distinct().ToList();
        if (versions.Count > 1)
        {
            var formatted = string.Join(", ", versions.Select(ToolVersionInfo.FormatMetricsVersion));
            msgs.Add(
                $"Warning: metrics versions differ across snapshots ({formatted}). Trend deltas may be unreliable.");
        }

        var profiles = snapshots.Select(s => s.Profile ?? SmellThresholdProfiles.DefaultProfileName).Distinct().ToList();
        if (profiles.Count > 1)
        {
            msgs.Add(
                $"Warning: profiles differ across snapshots ({string.Join(", ", profiles)}). "
                + "Trend smell deltas may be unreliable.");
        }

        return string.Join(" ", msgs);
    }

    static string LoadTemplate()
    {
        using var stream = typeof(TrendHtmlFormatter).Assembly
            .GetManifestResourceStream("Unilyze.Templates.trend.html")
            ?? throw new InvalidOperationException("Embedded resource not found: Unilyze.Templates.trend.html");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
