using Unilyze.History;
using System.Text;

namespace Unilyze.Output;

internal static class TrendChartSeries
{
    internal static void AppendPolylines(
        StringBuilder builder,
        (double?[] Values, string Color)[] series,
        Func<int, double> xAt,
        Func<double, double> yAt,
        Func<double, string> format)
    {
        foreach (var (values, color) in series)
        {
            foreach (var segment in Segments(values))
            {
                var points = string.Join(' ',
                    segment.Select(point => $"{format(xAt(point.Index))},{format(yAt(point.Value))}"));
                builder.Append("<polyline fill=\"none\" stroke=\"").Append(color)
                    .Append("\" stroke-width=\"2\" points=\"").Append(points).Append("\"/>");
            }
        }
    }

    internal static void AppendPoints(
        StringBuilder builder,
        (double?[] Values, string Color)[] series,
        IReadOnlyList<TrendSnapshot> snapshots,
        string chartId,
        Func<int, string> xAt,
        Func<double, string> yAt,
        Func<TrendSnapshot, string> tooltip)
    {
        for (var i = 0; i < snapshots.Count; i++)
        {
            foreach (var (values, color) in series)
            {
                if (values[i] is not { } value)
                    continue;

                var radius = snapshots.Count == 1 ? 6 : 4;
                builder.Append("<circle class=\"point\" cx=\"").Append(xAt(i))
                    .Append("\" cy=\"").Append(yAt(value))
                    .Append("\" r=\"").Append(radius).Append("\" fill=\"").Append(color)
                    .Append("\" data-index=\"").Append(i).Append("\" data-chart=\"").Append(chartId).Append("\">")
                    .Append("<title>").Append(tooltip(snapshots[i])).Append("</title></circle>");
            }
        }
    }

    internal static double?[] EnergyValues(IReadOnlyList<TrendSnapshot> snapshots)
        => snapshots
            .Select(snapshot => snapshot.HotPathMethodCount > 0
                ? (double?)snapshot.HotPathSmellCount / snapshot.HotPathMethodCount
                : null)
            .ToArray();

    internal static string RenderEnergy(
        IReadOnlyList<TrendSnapshot> snapshots,
        List<int> crossings)
    {
        var values = EnergyValues(snapshots);
        var max = values.OfType<double>().DefaultIfEmpty(0).Max();
        return TrendChartRenderer.RenderChart(
            "chart-energy",
            snapshots,
            [(values, "#22d3ee")],
            crossings,
            null,
            [0, Math.Ceiling(max * 2) / 4, max]);
    }

    internal static IEnumerable<List<(int Index, double Value)>> Segments(double?[] values)
    {
        var segment = new List<(int Index, double Value)>();
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] is { } value)
            {
                segment.Add((i, value));
                continue;
            }

            if (segment.Count == 0)
                continue;

            yield return segment;
            segment = [];
        }

        if (segment.Count > 0)
            yield return segment;
    }
}
