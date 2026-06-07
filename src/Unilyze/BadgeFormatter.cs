using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unilyze;

internal sealed record ShieldsBadge(int SchemaVersion, string Label, string Message, string Color);

internal enum BadgeMetric { CodeHealth, Mi, Smells }

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
            return new ShieldsBadge(1, label, "n/a", "lightgrey");

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
                }),
            BadgeMetric.Mi => new ShieldsBadge(
                1,
                label,
                s.AverageMaintainabilityIndex.ToString("F0", CultureInfo.InvariantCulture),
                s.AverageMaintainabilityIndex switch
                {
                    >= 80.0 => "brightgreen",
                    >= 60.0 => "yellow",
                    _ => "red"
                }),
            BadgeMetric.Smells => new ShieldsBadge(
                1,
                label,
                s.WarningCount.ToString(CultureInfo.InvariantCulture),
                s.CriticalCount > 0 ? "red" : s.WarningCount > 0 ? "yellow" : "brightgreen"),
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

    internal static string Serialize(ShieldsBadge badge) =>
        JsonSerializer.Serialize(badge, BadgeJsonContext.Default.ShieldsBadge);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ShieldsBadge))]
internal partial class BadgeJsonContext : JsonSerializerContext;
