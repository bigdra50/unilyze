using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unilyze.Dup;

internal static class DupFormatter
{
    public static string FormatMarkdown(CloneReport report)
    {
        var sb = new StringBuilder();
        var summary = report.Summary;
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "Duplication: {0:F1}% ({1}/{2} lines, {3} clone classes, {4} suppressed pairs)",
            summary.DuplicationPercent, summary.DuplicatedLines, summary.TotalLines,
            summary.CloneClassCount, summary.SuppressedPairCount));
        sb.AppendLine();

        if (report.CloneClasses.Count == 0)
        {
            sb.AppendLine("No duplicated code detected.");
            return sb.ToString();
        }

        foreach (var cloneClass in report.CloneClasses)
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "## Clone {0} ({1} tokens)", cloneClass.Id, cloneClass.TokenCount));
            foreach (var occurrence in cloneClass.Occurrences)
            {
                sb.AppendLine($"- `{occurrence.File}:{occurrence.StartLine}-{occurrence.EndLine}`");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string FormatJson(CloneReport report) =>
        JsonSerializer.Serialize(report, DupJsonContext.Default.CloneReport);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(CloneReport))]
[JsonSerializable(typeof(CloneSummary))]
[JsonSerializable(typeof(CloneClass))]
[JsonSerializable(typeof(CloneOccurrence))]
internal partial class DupJsonContext : JsonSerializerContext;
