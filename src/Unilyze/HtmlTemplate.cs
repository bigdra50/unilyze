using System.Text;

namespace Unilyze;

internal static class HtmlTemplate
{
    // The viewer markup lives in Templates/viewer.html (embedded resource) so it can be
    // edited with HTML tooling instead of inside a 2,400-line C# string literal.
    internal static string Value { get; } = LoadEmbedded("Unilyze.Templates.viewer.html");

    internal static string VendorScripts { get; } = BuildVendorScripts();

    static string BuildVendorScripts()
    {
        var sb = new StringBuilder();
        AppendInlineScript(sb,
            "Unilyze.Templates.vendor.cytoscape.min.js",
            "<!-- Cytoscape.js 3.30.4 - Copyright (c) 2016-2024 The Cytoscape Consortium - MIT License -->");
        AppendInlineScript(sb,
            "Unilyze.Templates.vendor.dagre.min.js",
            "<!-- dagre 0.8.5 - Copyright (c) 2014 Chris Pettitt - MIT License -->");
        AppendInlineScript(sb,
            "Unilyze.Templates.vendor.cytoscape-dagre.js",
            "<!-- cytoscape-dagre 2.5.0 - Copyright (c) 2016-2021 Max Franz - MIT License -->");
        return sb.ToString();
    }

    static void AppendInlineScript(StringBuilder sb, string resourceName, string attributionComment)
    {
        var payload = LoadEmbedded(resourceName)
            .Replace("</script", "<\\/script", StringComparison.Ordinal);
        sb.AppendLine(attributionComment);
        sb.Append("<script>");
        sb.Append(payload);
        sb.AppendLine("</script>");
    }

    static string LoadEmbedded(string resourceName)
    {
        using var stream = typeof(HtmlTemplate).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
