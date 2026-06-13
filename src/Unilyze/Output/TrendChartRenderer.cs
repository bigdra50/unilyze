using Unilyze.History;
using Unilyze.Config;
using Unilyze.Cli;
using System.Globalization;
using System.Text;

namespace Unilyze.Output;

internal static class TrendChartRenderer
{
    const int Width = 900;
    const int Height = 200;
    const int PadLeft = 48;
    const int PadRight = 16;
    const int PadTop = 16;
    const int PadBottom = 36;
    public static (string Health, string Smells, string Energy, string Types) RenderAll(
        IReadOnlyList<TrendSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            const string empty = "<p>No snapshots</p>";
            return (empty, empty, empty, empty);
        }

        var crossings = DetectCrossings(snapshots);
        return (
            RenderHealthChart(snapshots, crossings),
            RenderSmellChart(snapshots, crossings),
            TrendChartSeries.RenderEnergy(snapshots, crossings),
            RenderTypeChart(snapshots, crossings));
    }
    static List<int> DetectCrossings(IReadOnlyList<TrendSnapshot> snapshots)
    {
        var crossings = new List<int>();
        for (var i = 1; i < snapshots.Count; i++)
        {
            var prev = snapshots[i - 1];
            var cur = snapshots[i];
            if (prev.MetricsVersion != cur.MetricsVersion
                || DefaultProfile(prev.Profile) != DefaultProfile(cur.Profile))
                crossings.Add(i);
        }
        return crossings;
    }

    static string DefaultProfile(string? profile) =>
        profile ?? SmellThresholdProfiles.DefaultProfileName;
    static string RenderHealthChart(IReadOnlyList<TrendSnapshot> snapshots, List<int> crossings)
    {
        var series = new[]
        {
            (Values: snapshots.Select(s => (double?)s.AverageCodeHealth).ToArray(), Color: "#4da3ff"),
            (Values: snapshots.Select(s => (double?)s.MinCodeHealth).ToArray(), Color: "#f59e0b"),
        };
        return RenderChart("chart-health", snapshots, series, crossings, fixedMax: 10, yTicks: [0, 2, 4, 6, 8, 10]);
    }

    static string RenderSmellChart(IReadOnlyList<TrendSnapshot> snapshots, List<int> crossings)
    {
        var series = new[]
        {
            (Values: snapshots.Select(s => (double?)s.WarningSmellCount).ToArray(), Color: "#fbbf24"),
            (Values: snapshots.Select(s => (double?)s.CriticalSmellCount).ToArray(), Color: "#ef4444"),
        };
        var max = series.SelectMany(s => s.Values).OfType<double>().DefaultIfEmpty(0).Max();
        var mid = Math.Ceiling(max / 2);
        return RenderChart("chart-smells", snapshots, series, crossings, fixedMax: null, yTicks: [0, mid, max]);
    }

    static string RenderTypeChart(IReadOnlyList<TrendSnapshot> snapshots, List<int> crossings)
    {
        var series = new[]
        {
            (Values: snapshots.Select(s => (double?)s.TypeCount).ToArray(), Color: "#34d399"),
            (Values: snapshots.Select(s => (double?)s.HighComplexityTypeCount).ToArray(), Color: "#a78bfa"),
        };
        var max = series.SelectMany(s => s.Values).OfType<double>().DefaultIfEmpty(0).Max();
        var mid = Math.Ceiling(max / 2);
        return RenderChart("chart-types", snapshots, series, crossings, fixedMax: null, yTicks: [0, mid, max]);
    }

    internal static string RenderChart(
        string chartId,
        IReadOnlyList<TrendSnapshot> snapshots,
        (double?[] Values, string Color)[] series,
        List<int> crossings,
        double? fixedMax,
        double[] yTicks)
    {
        var n = snapshots.Count;
        var innerW = Width - PadLeft - PadRight;
        var innerH = Height - PadTop - PadBottom;
        var presentValues = series.SelectMany(s => s.Values).OfType<double>().ToList();
        var lo = fixedMax.HasValue ? 0 : Math.Min(0, presentValues.DefaultIfEmpty(0).Min());
        var hi = fixedMax ?? Math.Max(1, presentValues.DefaultIfEmpty(0).Max());
        var range = hi - lo;
        if (range <= 0) range = 1;

        double XAt(int i) => PadLeft + (n <= 1 ? innerW / 2.0 : i / (double)(n - 1) * innerW);
        double YAt(double v) => PadTop + innerH - (v - lo) / range * innerH;

        var tickStep = n > 50 ? (int)Math.Ceiling(n / 12.0) : 1;
        var sb = new StringBuilder();
        sb.Append("<svg class=\"chart\" viewBox=\"0 0 ").Append(Width).Append(' ').Append(Height)
            .Append("\" data-chart=\"").Append(chartId).Append("\">");

        sb.Append("<g stroke=\"#2d3a4d\" fill=\"#8b9cb3\" font-size=\"10\">");
        sb.Append("<path fill=\"none\" d=\"M").Append(PadLeft).Append(',').Append(PadTop + innerH)
            .Append(" H").Append(PadLeft + innerW).Append("\"/>");
        sb.Append("<path fill=\"none\" d=\"M").Append(PadLeft).Append(',').Append(PadTop)
            .Append(" V").Append(PadTop + innerH).Append("\"/>");

        foreach (var tick in yTicks.Distinct().OrderBy(t => t))
        {
            var y = YAt(tick);
            sb.Append("<line x1=\"").Append(PadLeft).Append("\" x2=\"").Append(PadLeft + innerW)
                .Append("\" y1=\"").Append(Fmt(y)).Append("\" y2=\"").Append(Fmt(y))
                .Append("\" stroke=\"#1f2937\"/>");
            sb.Append("<text x=\"").Append(PadLeft - 6).Append("\" y=\"").Append(Fmt(y + 3))
                .Append("\" text-anchor=\"end\">").Append(Fmt(tick)).Append("</text>");
        }

        for (var i = 0; i < n; i += tickStep)
        {
            var x = XAt(i);
            sb.Append("<line x1=\"").Append(Fmt(x)).Append("\" x2=\"").Append(Fmt(x))
                .Append("\" y1=\"").Append(PadTop + innerH).Append("\" y2=\"").Append(PadTop + innerH + 4).Append("\"/>");
            string label;
            if (snapshots[i].SourceFile is { Length: > 0 } file)
            {
                var stem = file.Replace(".json", "", StringComparison.OrdinalIgnoreCase);
                label = stem.Length > 8 ? stem[^8..] : stem;
            }
            else
                label = snapshots[i].AnalyzedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            sb.Append("<text x=\"").Append(Fmt(x)).Append("\" y=\"").Append(PadTop + innerH + 16)
                .Append("\" text-anchor=\"middle\">").Append(Escape(label)).Append("</text>");
        }
        sb.Append("</g>");

        foreach (var crossing in crossings)
        {
            var x = XAt(crossing);
            var prev = snapshots[crossing - 1];
            var cur = snapshots[crossing];
            var reasons = new List<string>();
            if (prev.MetricsVersion != cur.MetricsVersion)
                reasons.Add($"metricsVersion {ToolVersionInfo.FormatMetricsVersion(prev.MetricsVersion)}→{ToolVersionInfo.FormatMetricsVersion(cur.MetricsVersion)}");
            if (DefaultProfile(prev.Profile) != DefaultProfile(cur.Profile))
                reasons.Add($"profile {DefaultProfile(prev.Profile)}→{DefaultProfile(cur.Profile)}");
            sb.Append("<line x1=\"").Append(Fmt(x)).Append("\" x2=\"").Append(Fmt(x))
                .Append("\" y1=\"").Append(PadTop).Append("\" y2=\"").Append(PadTop + innerH)
                .Append("\" stroke=\"#f87171\" stroke-dasharray=\"4 3\" stroke-width=\"1.5\">");
            sb.Append("<title>").Append(Escape(string.Join(", ", reasons))).Append("</title></line>");
        }

        TrendChartSeries.AppendPolylines(sb, series, XAt, YAt, Fmt);
        TrendChartSeries.AppendPoints(
            sb, series, snapshots, chartId,
            i => XAt(i).ToString("0.##", CultureInfo.InvariantCulture),
            value => YAt(value).ToString("0.##", CultureInfo.InvariantCulture),
            snapshot => Escape(BuildTooltip(snapshot)));

        sb.Append("</svg>");
        return sb.ToString();
    }

    static string BuildTooltip(TrendSnapshot s) =>
        $"{s.SourceFile ?? "(unknown file)"}\n"
        + $"{s.AnalyzedAt:yyyy-MM-dd HH:mm}\n"
        + $"CodeHealth avg/min: {s.AverageCodeHealth} / {s.MinCodeHealth}\n"
        + $"Smells: {s.CodeSmellCount} (warn {s.WarningSmellCount}, crit {s.CriticalSmellCount})\n"
        + $"Energy pressure: {(s.HotPathMethodCount > 0 ? Fmt(s.HotPathSmellCount / (double)s.HotPathMethodCount) : "n/a")}\n"
        + $"Types: {s.TypeCount}, high CC: {s.HighComplexityTypeCount}\n"
        + $"metricsVersion: {ToolVersionInfo.FormatMetricsVersion(s.MetricsVersion)}, profile: {DefaultProfile(s.Profile)}";

    static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
