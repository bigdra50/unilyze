using System.Net;
using System.Text;

namespace Unilyze.Serve;

/// <summary>
/// Routes a serve HTTP request and enforces the security boundary on every path:
/// exact loopback Host, same-origin (when an Origin is present), and a bearer token on
/// all <c>/api/*</c> calls. <c>GET /</c> returns the no-store viewer HTML with the token
/// embedded in the body (never in the URL). API endpoints are added by later issues.
/// </summary>
internal sealed class ServeHttpHandler
{
    static readonly TimeSpan LongPollTimeout = TimeSpan.FromSeconds(25);

    // default-src 'none' denies everything not explicitly allowed; scripts/vendor/styles are
    // same-origin; style stays 'unsafe-inline' until the viewer's inline style attrs are removed.
    const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; connect-src 'self'; worker-src 'self'; "
        + "style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; "
        + "base-uri 'none'; form-action 'none'";

    readonly ServeAuth _auth;
    readonly SnapshotStore _store;
    readonly string _title;

    public ServeHttpHandler(ServeAuth auth, SnapshotStore store, string title)
    {
        _auth = auth;
        _store = store;
        _title = title;
    }

    public void Handle(HttpListenerContext context)
    {
        var request = context.Request;

        // DNS-rebinding defense: reject anything whose Host is not our exact loopback host.
        // (HttpListener already 404s a non-127.0.0.1 Host; this also catches a wrong port.)
        if (!_auth.IsHostAllowed(request))
        {
            WriteStatus(context, 403, "Forbidden: host not allowed");
            return;
        }

        if (!_auth.IsOriginAllowed(request))
        {
            WriteStatus(context, 403, "Forbidden: cross-origin request");
            return;
        }

        var path = request.Url?.AbsolutePath ?? "/";

        if (path is "/" or "/index.html")
        {
            HandleIndex(context);
            return;
        }

        // Static assets carry no secrets and are loaded via <script src>/<link>, which cannot
        // attach an Authorization header — so they are reachable without the bearer token.
        if (path.StartsWith("/static/", StringComparison.Ordinal))
        {
            if (!ServeStaticAssets.TryHandle(context, path))
                WriteStatus(context, 404, "Not found");
            return;
        }

        if (path.StartsWith("/api/", StringComparison.Ordinal))
        {
            if (!_auth.IsAuthorized(request))
            {
                WriteStatus(context, 401, "Unauthorized");
                return;
            }

            HandleApi(context, path);
            return;
        }

        WriteStatus(context, 404, "Not found");
    }

    void HandleIndex(HttpListenerContext context)
    {
        var html = BuildIndexHtml();
        var body = Encoding.UTF8.GetBytes(html);
        var response = context.Response;
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body, 0, body.Length);
        response.OutputStream.Close();
    }

    string BuildIndexHtml() =>
        LoadResource("Unilyze.Templates.serve.index.html")
            .Replace("__TOKEN__", _auth.Token)
            .Replace("__TITLE__", WebUtility.HtmlEncode(_title));

    static string LoadResource(string resourceName)
    {
        using var stream = typeof(ServeHttpHandler).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    void HandleApi(HttpListenerContext context, string path)
    {
        if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.Ordinal))
        {
            WriteStatus(context, 405, "Method not allowed");
            return;
        }

        switch (path)
        {
            case "/api/state":
                HandleState(context);
                break;
            case "/api/snapshot":
                HandleSnapshot(context);
                break;
            default:
                WriteStatus(context, 404, "Not found");
                break;
        }
    }

    void HandleState(HttpListenerContext context)
    {
        var after = ParseAfter(context.Request.QueryString["after"]);
        var state = _store.WaitForChange(after, LongPollTimeout);
        WriteJson(context, 200, ServeStateJson.Build(state));
    }

    void HandleSnapshot(HttpListenerContext context)
    {
        var snapshot = _store.Current;
        if (snapshot is null)
        {
            WriteStatus(context, 503, "No snapshot yet");
            return;
        }

        var ifNoneMatch = context.Request.Headers["If-None-Match"];
        var response = context.Response;
        response.Headers["ETag"] = snapshot.ETag;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";

        if (ifNoneMatch is not null && string.Equals(ifNoneMatch, snapshot.ETag, StringComparison.Ordinal))
        {
            response.StatusCode = 304;
            response.OutputStream.Close();
            return;
        }

        response.StatusCode = 200;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = snapshot.JsonBytes.Length;
        response.OutputStream.Write(snapshot.JsonBytes, 0, snapshot.JsonBytes.Length);
        response.OutputStream.Close();
    }

    static long ParseAfter(string? raw) =>
        long.TryParse(raw, out var value) ? value : -1;

    static void WriteJson(HttpListenerContext context, int status, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var response = context.Response;
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body, 0, body.Length);
        response.OutputStream.Close();
    }

    static void WriteStatus(HttpListenerContext context, int status, string message)
    {
        var body = Encoding.UTF8.GetBytes(message + "\n");
        var response = context.Response;
        response.StatusCode = status;
        response.ContentType = "text/plain; charset=utf-8";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body, 0, body.Length);
        response.OutputStream.Close();
    }
}
