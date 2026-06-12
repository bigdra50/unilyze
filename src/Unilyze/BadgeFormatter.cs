using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unilyze;

// AnalysisLevel is an extra, non-standard field. shields.io ignores unknown keys in endpoint
// JSON, so it is safe to surface the analysis depth alongside the standard badge fields (issue 16).
internal sealed record ShieldsBadge(
    int SchemaVersion, string Label, string Message, string Color, string? AnalysisLevel = null);

internal enum BadgeMetric { CodeHealth, Mi, Smells, Energy, Dup }

internal enum BadgeFormat { Json, Svg }

internal static class BadgeFormatter
{
    internal static ShieldsBadge Build(BadgeMetric metric, StatuslineFormatter.Summary s) =>
        Build(metric, s, duplicationPercent: null);

    internal static ShieldsBadge Build(BadgeMetric metric, StatuslineFormatter.Summary s, double? duplicationPercent)
    {
        var label = metric switch
        {
            BadgeMetric.CodeHealth => "code health",
            BadgeMetric.Mi => "maintainability",
            BadgeMetric.Smells => "smells",
            BadgeMetric.Energy => "energy pressure",
            BadgeMetric.Dup => "duplication",
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null)
        };

        if (metric == BadgeMetric.Dup)
            return BuildDupBadge(label, duplicationPercent, s.AnalysisLevel);

        if (metric == BadgeMetric.Energy && s.HotPathMethodCount == 0)
            return new ShieldsBadge(1, label, "n/a", "lightgrey", s.AnalysisLevel);

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
            BadgeMetric.Energy => new ShieldsBadge(
                1,
                label,
                EnergyPressure(s).ToString("F2", CultureInfo.InvariantCulture),
                EnergyPressure(s) switch
                {
                    0 => "brightgreen",
                    < 1.0 => "yellow",
                    _ => "red"
                },
                s.AnalysisLevel),
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null)
        };
    }

    static double EnergyPressure(StatuslineFormatter.Summary summary)
        => summary.HotPathSmellCount / (double)summary.HotPathMethodCount;

    static ShieldsBadge BuildDupBadge(string label, double? duplicationPercent, string? analysisLevel)
    {
        if (duplicationPercent is null)
            return new ShieldsBadge(1, label, "n/a", "lightgrey", analysisLevel);

        var message = string.Format(CultureInfo.InvariantCulture, "{0:F1}%", duplicationPercent.Value);
        var color = duplicationPercent.Value switch
        {
            < 3.0 => "brightgreen",
            < 10.0 => "yellow",
            _ => "red"
        };
        return new ShieldsBadge(1, label, message, color, analysisLevel);
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

        if (value.Equals("energy", StringComparison.OrdinalIgnoreCase))
        {
            metric = BadgeMetric.Energy;
            return true;
        }

        if (value.Equals("dup", StringComparison.OrdinalIgnoreCase))
        {
            metric = BadgeMetric.Dup;
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
