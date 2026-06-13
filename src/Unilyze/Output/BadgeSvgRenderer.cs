using Unilyze.Discovery;
using System.Globalization;
using System.Text;

namespace Unilyze.Output;

internal static class BadgeSvgRenderer
{
    static readonly double[] CharWidths =
    [
        3.9, 4.3, 5.1, 9.1, 7.0, 12.5, 7.9, 3.0, 4.8, 4.8, 7.0, 9.1, 4.0, 5.0, 4.0, 5.0,
        7.0, 7.0, 7.0, 7.0, 7.0, 7.0, 7.0, 7.0, 7.0, 7.0, 4.8, 4.8, 9.1, 9.1, 9.1, 6.0,
        11.0, 7.6, 7.6, 7.8, 8.5, 7.0, 6.4, 8.6, 8.4, 4.6, 5.0, 7.7, 6.1, 9.6, 8.4, 8.8,
        6.7, 8.8, 7.7, 7.6, 6.8, 8.2, 7.6, 11.0, 7.6, 6.8, 7.6, 4.8, 5.0, 4.8, 9.1, 7.0,
        7.0, 6.8, 7.0, 5.9, 7.0, 6.7, 3.9, 7.0, 7.0, 3.1, 3.7, 6.5, 3.1, 10.8, 7.0, 6.9,
        7.0, 7.0, 4.7, 5.9, 4.4, 7.0, 6.5, 9.5, 6.5, 6.5, 5.9, 7.0, 5.0, 7.0, 9.1
    ];

    internal static string Render(ShieldsBadge badge)
    {
        var label = badge.Label;
        var message = badge.Message;
        var hex = ResolveColor(badge.Color);

        var labelTextWidth = Measure(label);
        var messageTextWidth = Measure(message);

        var lw = (int)Math.Ceiling(labelTextWidth) + 10;
        var mw = (int)Math.Ceiling(messageTextWidth) + 10;
        var w = lw + mw;
        var lx = lw * 5;
        var mx = lw * 10 + mw * 5;
        var ltl = (int)Math.Round(labelTextWidth * 10);
        var mtl = (int)Math.Round(messageTextWidth * 10);

        var escapedLabel = EscapeXml(label);
        var escapedMessage = EscapeXml(message);
        var ariaLabel = $"{escapedLabel}: {escapedMessage}";

        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"");
        sb.Append(w.ToString(inv));
        sb.Append("\" height=\"20\" role=\"img\" aria-label=\"");
        sb.Append(ariaLabel);
        sb.Append("\">");

        // Embed the analysis level as an XML comment inside the root so degraded badges are
        // traceable, while keeping the output starting with the <svg> element (issue 16).
        if (!string.IsNullOrEmpty(badge.AnalysisLevel))
        {
            sb.Append("<!-- unilyze analysisLevel: ");
            sb.Append(EscapeXmlComment(badge.AnalysisLevel));
            sb.Append(" -->");
        }

        sb.Append("<title>");
        sb.Append(ariaLabel);
        sb.Append("</title><linearGradient id=\"s\" x2=\"0\" y2=\"100%\"><stop offset=\"0\" stop-color=\"#bbb\" stop-opacity=\".1\"/><stop offset=\"1\" stop-opacity=\".1\"/></linearGradient><clipPath id=\"r\"><rect width=\"");
        sb.Append(w.ToString(inv));
        sb.Append("\" height=\"20\" rx=\"3\" fill=\"#fff\"/></clipPath><g clip-path=\"url(#r)\"><rect width=\"");
        sb.Append(lw.ToString(inv));
        sb.Append("\" height=\"20\" fill=\"#555\"/><rect x=\"");
        sb.Append(lw.ToString(inv));
        sb.Append("\" width=\"");
        sb.Append(mw.ToString(inv));
        sb.Append("\" height=\"20\" fill=\"");
        sb.Append(hex);
        sb.Append("\"/><rect width=\"");
        sb.Append(w.ToString(inv));
        sb.Append("\" height=\"20\" fill=\"url(#s)\"/></g><g fill=\"#fff\" text-anchor=\"middle\" font-family=\"Verdana,Geneva,DejaVu Sans,sans-serif\" text-rendering=\"geometricPrecision\" font-size=\"110\"><text aria-hidden=\"true\" x=\"");
        sb.Append(lx.ToString(inv));
        sb.Append("\" y=\"150\" fill=\"#010101\" fill-opacity=\".3\" transform=\"scale(.1)\" textLength=\"");
        sb.Append(ltl.ToString(inv));
        sb.Append("\">");
        sb.Append(escapedLabel);
        sb.Append("</text><text x=\"");
        sb.Append(lx.ToString(inv));
        sb.Append("\" y=\"140\" transform=\"scale(.1)\" textLength=\"");
        sb.Append(ltl.ToString(inv));
        sb.Append("\">");
        sb.Append(escapedLabel);
        sb.Append("</text><text aria-hidden=\"true\" x=\"");
        sb.Append(mx.ToString(inv));
        sb.Append("\" y=\"150\" fill=\"#010101\" fill-opacity=\".3\" transform=\"scale(.1)\" textLength=\"");
        sb.Append(mtl.ToString(inv));
        sb.Append("\">");
        sb.Append(escapedMessage);
        sb.Append("</text><text x=\"");
        sb.Append(mx.ToString(inv));
        sb.Append("\" y=\"140\" transform=\"scale(.1)\" textLength=\"");
        sb.Append(mtl.ToString(inv));
        sb.Append("\">");
        sb.Append(escapedMessage);
        sb.Append("</text></g></svg>");
        return sb.ToString();
    }

    static double Measure(string text)
    {
        var sum = 0.0;
        foreach (var rune in text.EnumerateRunes())
        {
            sum += rune.Value is >= 0x20 and <= 0x7E
                ? CharWidths[rune.Value - 0x20]
                : 11.0;
        }

        return sum;
    }

    // XML comments cannot contain "--"; collapse any occurrence defensively.
    static string EscapeXmlComment(string text) =>
        text.Replace("--", "-");

    static string EscapeXml(string text) =>
        text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");

    static string ResolveColor(string color)
    {
        if (IsHexColor(color))
            return color;

        return color.ToLowerInvariant() switch
        {
            "brightgreen" => "#4c1",
            "green" => "#97ca00",
            "yellowgreen" => "#a4a61d",
            "yellow" => "#dfb317",
            "orange" => "#fe7d37",
            "red" => "#e05d44",
            "lightgrey" => "#9f9f9f",
            "grey" => "#555",
            "blue" => "#007ec6",
            _ => "#9f9f9f"
        };
    }

    static bool IsHexColor(string color)
    {
        if (color.Length is not (4 or 5 or 7 or 9) || color[0] != '#')
            return false;

        for (var i = 1; i < color.Length; i++)
        {
            if (!char.IsAsciiHexDigit(color[i]))
                return false;
        }

        return true;
    }
}
