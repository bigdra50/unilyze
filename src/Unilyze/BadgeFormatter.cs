using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unilyze;

// AnalysisLevel is an extra, non-standard field. shields.io ignores unknown keys in endpoint
// JSON, so it is safe to surface the analysis depth alongside the standard badge fields (issue 16).
internal sealed record ShieldsBadge(
    int SchemaVersion, string Label, string Message, string Color, string? AnalysisLevel = null);

internal enum BadgeMetric { CodeHealth, Mi, Smells }

internal enum BadgeFormat { Json, Svg }

internal static class BadgeFormatter
{
    internal static ShieldsBadge Build(BadgeMetric metric, StatuslineFormatter.Summary s)
    {
        var label = metric switch
        {
            BadgeMetric.CodeHealth => "code health",
            BadgeMetric.Mi => "maintainability",
            BadgeMetric.Smells => "smells",
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null)
        };

        if (s.TypeCount == 0)
            return new ShieldsBadge(1, label, "n/a", "lightgrey", s.AnalysisLevel);

        return metric switch
        {
            BadgeMetric.CodeHealth => new ShieldsBadge(
                1,
                label,
                string.Format(CultureInfo.InvariantCulture, "{0:F1} / {1:F1}", s.AverageCodeHealth, s.MinCodeHealth),
                s.MinCodeHealth switch
                {
                    >= 8.0 => "brightgreen",
                    >= 5.0 => "yellow",
                    _ => "red"
                },
                s.AnalysisLevel),
            BadgeMetric.Mi => new ShieldsBadge(
                1,
                label,
                s.AverageMaintainabilityIndex.ToString("F0", CultureInfo.InvariantCulture),
                s.AverageMaintainabilityIndex switch
                {
                    >= 80.0 => "brightgreen",
                    >= 60.0 => "yellow",
                    _ => "red"
                },
                s.AnalysisLevel),
            BadgeMetric.Smells => new ShieldsBadge(
                1,
                label,
                s.WarningCount.ToString(CultureInfo.InvariantCulture),
                s.CriticalCount > 0 ? "red" : s.WarningCount > 0 ? "yellow" : "brightgreen",
                s.AnalysisLevel),
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null)
        };
    }

    internal static bool TryParseMetric(string? value, out BadgeMetric metric)
    {
        metric = BadgeMetric.CodeHealth;

        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (value.Equals("codehealth", StringComparison.OrdinalIgnoreCase))
        {
            metric = BadgeMetric.CodeHealth;
            return true;
        }

        if (value.Equals("mi", StringComparison.OrdinalIgnoreCase))
        {
            metric = BadgeMetric.Mi;
            return true;
        }

        if (value.Equals("smells", StringComparison.OrdinalIgnoreCase))
        {
            metric = BadgeMetric.Smells;
            return true;
        }

        return false;
    }

    internal static bool TryParseFormat(string? value, out BadgeFormat format)
    {
        format = BadgeFormat.Json;

        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (value.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            format = BadgeFormat.Json;
            return true;
        }

        if (value.Equals("svg", StringComparison.OrdinalIgnoreCase))
        {
            format = BadgeFormat.Svg;
            return true;
        }

        return false;
    }

    internal static string Serialize(ShieldsBadge badge) =>
        JsonSerializer.Serialize(badge, BadgeJsonContext.Default.ShieldsBadge);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ShieldsBadge))]
internal partial class BadgeJsonContext : JsonSerializerContext;
