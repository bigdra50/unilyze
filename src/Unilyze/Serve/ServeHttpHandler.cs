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

    readonly ServeAuth _auth;
    readonly SnapshotStore _store;

    public ServeHttpHandler(ServeAuth auth, SnapshotStore store)
    {
        _auth = auth;
        _store = store;
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
        var html = BuildBootstrapHtml();
        var body = Encoding.UTF8.GetBytes(html);
        var response = context.Response;
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body, 0, body.Length);
        response.OutputStream.Close();
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

    string BuildBootstrapHtml() =>
        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">"
        + "<title>unilyze serve</title>"
        + $"<script>window.__UNILYZE_TOKEN__={JsonStringLiteral(_auth.Token)};</script>"
        + "</head><body><p>unilyze serve is starting.</p></body></html>";

    static string JsonStringLiteral(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '<': sb.Append("\\u003c"); break;
                case '>': sb.Append("\\u003e"); break;
                case '&': sb.Append("\\u0026"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

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
