using Unilyze.Diff;
using System.Globalization;
using System.Text;

namespace Unilyze.Output;

internal static class MarkdownDiffFormatter
{
    static readonly HashSet<string> HigherIsBetter = ["CodeHealth", "AverageMaintainabilityIndex", "MinMaintainabilityIndex"];
    const double Epsilon = 0.0001;

    internal static string Generate(
        DiffResult diff,
        StatuslineFormatter.Summary before,
        StatuslineFormatter.Summary after,
        DiffGateResult? gate,
        double? deltaThreshold = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(VerdictLine(diff, gate, deltaThreshold));
        sb.AppendLine();
        AppendCodeHealthTable(sb, before, after);
        sb.AppendLine();
        AppendSmellTable(sb, before, after);
        sb.AppendLine();
        AppendTypeCountTable(sb, diff);
        sb.AppendLine();
        MarkdownDeltaScoreFormatter.Append(sb, diff);
        AppendWorstDegraded(sb, diff);
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    static string VerdictLine(DiffResult diff, DiffGateResult? gate, double? deltaThreshold)
    {
        if (gate?.HasRegression == true)
            return $"**Verdict:** FAIL — {gate.Reason}";

        if (deltaThreshold.HasValue && diff.DeltaScore < deltaThreshold.Value)
            return $"**Verdict:** FAIL — deltaScore {Fmt(diff.DeltaScore)} is below {Fmt(deltaThreshold.Value)}";

        if (gate != null || deltaThreshold.HasValue)
            return "**Verdict:** PASS";

        var s = diff.Summary;
        return $"**Verdict:** {s.DegradedCount} degraded, {s.ImprovedCount} improved, {s.UnchangedCount} unchanged";
    }

    static void AppendCodeHealthTable(StringBuilder sb, StatuslineFormatter.Summary before, StatuslineFormatter.Summary after)
    {
        sb.AppendLine("### Code Health");
        sb.AppendLine();
        sb.AppendLine("| Metric | Before | After | Delta |");
        sb.AppendLine("| --- | --- | --- | --- |");
        AppendDoubleRow(sb, "Avg CH", before.AverageCodeHealth, after.AverageCodeHealth);
        AppendDoubleRow(sb, "Min CH", before.MinCodeHealth, after.MinCodeHealth);
    }

    static void AppendSmellTable(StringBuilder sb, StatuslineFormatter.Summary before, StatuslineFormatter.Summary after)
    {
        sb.AppendLine("### Smells");
        sb.AppendLine();
        sb.AppendLine("| Metric | Before | After | Delta |");
        sb.AppendLine("| --- | --- | --- | --- |");
        AppendIntRow(sb, "Warnings", before.WarningCount, after.WarningCount);
        AppendIntRow(sb, "Critical", before.CriticalCount, after.CriticalCount);
    }

    static void AppendTypeCountTable(StringBuilder sb, DiffResult diff)
    {
        sb.AppendLine("### Types");
        sb.AppendLine();
        sb.AppendLine("| Metric | Count |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| Degraded | {diff.Summary.DegradedCount} |");
        sb.AppendLine($"| Improved | {diff.Summary.ImprovedCount} |");
    }

    static void AppendWorstDegraded(StringBuilder sb, DiffResult diff)
    {
        var entries = diff.Degraded
            .Select(t =>
            {
                var worst = FindWorstMetric(t);
                return worst is null
                    ? ((TypeDiff Type, string Metric, double Severity, string Delta)?)null
                    : (Type: t, worst.Value.Metric, worst.Value.Severity, worst.Value.Delta);
            })
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .OrderByDescending(x => x.Severity)
            .ThenBy(x => x.Metric, StringComparer.Ordinal)
            .ThenBy(x => x.Type.TypeName, StringComparer.Ordinal)
            .Take(5)
            .ToList();

        if (entries.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("### Worst degraded types");
        sb.AppendLine();
        sb.AppendLine("| Type | Metric | Delta |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var e in entries)
            sb.AppendLine($"| {e.Type.TypeName} | {e.Metric} | {e.Delta} |");
    }

    static (string Metric, double Severity, string Delta)? FindWorstMetric(TypeDiff type)
    {
        (string Metric, double Severity, string Delta)? worst = null;

        foreach (var d in type.DoubleDeltas)
        {
            if (!IsOffending(d.Name, d.Delta))
                continue;
            var severity = Math.Abs(HigherIsBetter.Contains(d.Name) ? -d.Delta : d.Delta);
            var candidate = (d.Name, severity, FormatDoubleDelta(d.Delta));
            if (worst is null || IsWorse(candidate, worst.Value))
                worst = candidate;
        }

        foreach (var d in type.IntDeltas)
        {
            if (!IsOffending(d.Name, d.Delta))
                continue;
            var severity = Math.Abs((double)d.Delta);
            var candidate = (d.Name, severity, FormatIntDelta(d.Delta));
            if (worst is null || IsWorse(candidate, worst.Value))
                worst = candidate;
        }

        return worst;
    }

    static bool IsWorse(
        (string Metric, double Severity, string Delta) candidate,
        (string Metric, double Severity, string Delta) current) =>
        candidate.Severity > current.Severity
        || (candidate.Severity == current.Severity && string.CompareOrdinal(candidate.Metric, current.Metric) < 0);

    static bool IsOffending(string name, double delta) =>
        Math.Abs(delta) > Epsilon && (HigherIsBetter.Contains(name) ? delta < 0 : delta > 0);

    static bool IsOffending(string name, int delta) =>
        delta != 0 && (HigherIsBetter.Contains(name) ? delta < 0 : delta > 0);

    static void AppendDoubleRow(StringBuilder sb, string label, double before, double after)
    {
        var delta = after - before;
        sb.AppendLine($"| {label} | {Fmt(before)} | {Fmt(after)} | {FormatDoubleDelta(delta)} |");
    }

    static void AppendIntRow(StringBuilder sb, string label, int before, int after)
    {
        var delta = after - before;
        sb.AppendLine($"| {label} | {before} | {after} | {FormatIntDelta(delta)} |");
    }

    static string FormatDoubleDelta(double delta)
    {
        if (Math.Abs(delta) <= Epsilon)
            return "0";
        var sign = delta > 0 ? "+" : "";
        return sign + Fmt(delta);
    }

    static string FormatIntDelta(int delta) =>
        delta > 0 ? $"+{delta}" : delta.ToString(CultureInfo.InvariantCulture);

    internal static string Fmt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
