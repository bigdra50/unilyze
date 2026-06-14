using System.Net;
using System.Text;

namespace Unilyze.Serve;

/// <summary>
/// Serves the serve-only viewer assets as individual same-origin resources under
/// <c>/static/</c> so a strict CSP (<c>script-src 'self'</c>) applies. The raw viewer
/// <c>main.js</c> carries the snapshot placeholders; in serve mode they are replaced with
/// <c>null</c> and the snapshot arrives over the API instead of being inlined.
/// </summary>
internal static class ServeStaticAssets
{
    sealed record Asset(string ResourceName, string ContentType, bool ReplaceDataPlaceholders);

    static readonly Dictionary<string, Asset> Map = new(StringComparer.Ordinal)
    {
        ["/static/main.js"] = new("Unilyze.Templates.viewer.main.js", "text/javascript; charset=utf-8", true),
        ["/static/serve-client.js"] = new("Unilyze.Templates.serve.serve-client.js", "text/javascript; charset=utf-8", false),
        ["/static/styles.css"] = new("Unilyze.Templates.viewer.styles.css", "text/css; charset=utf-8", false),
        ["/static/vendor/cytoscape.min.js"] = new("Unilyze.Templates.vendor.cytoscape.min.js", "text/javascript; charset=utf-8", false),
        ["/static/vendor/dagre.min.js"] = new("Unilyze.Templates.vendor.dagre.min.js", "text/javascript; charset=utf-8", false),
        ["/static/vendor/cytoscape-dagre.js"] = new("Unilyze.Templates.vendor.cytoscape-dagre.js", "text/javascript; charset=utf-8", false),
        ["/static/vendor/elk.bundled.js"] = new("Unilyze.Templates.vendor.elk.bundled.js", "text/javascript; charset=utf-8", false),
    };

    public static bool TryHandle(HttpListenerContext context, string path)
    {
        if (!Map.TryGetValue(path, out var asset))
            return false;

        var text = LoadResource(asset.ResourceName);
        if (asset.ReplaceDataPlaceholders)
        {
            text = text
                .Replace("__DATA_PLACEHOLDER__", "null")
                .Replace("__DIFF_DATA_PLACEHOLDER__", "null");
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        var response = context.Response;
        response.StatusCode = 200;
        response.ContentType = asset.ContentType;
        response.Headers["X-Content-Type-Options"] = "nosniff";
        // Per-start token rotation makes long-lived caching unsafe; keep assets fresh.
        response.Headers["Cache-Control"] = "no-cache";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Close();
        return true;
    }

    static string LoadResource(string resourceName)
    {
        using var stream = typeof(ServeStaticAssets).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
