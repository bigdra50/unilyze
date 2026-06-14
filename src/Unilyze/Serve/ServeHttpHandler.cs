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
    static readonly TimeSpan LongPollHeartbeat = TimeSpan.FromSeconds(1);
    static readonly byte[] JsonWhitespace = [(byte)' '];
    const int MaxConcurrentLongPolls = 16;

    // default-src 'none' denies everything not explicitly allowed; scripts/vendor/styles are
    // same-origin; style stays 'unsafe-inline' until the viewer's inline style attrs are removed.
    const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; connect-src 'self'; worker-src 'self'; "
        + "style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; "
        + "base-uri 'none'; form-action 'none'";

    readonly ServeAuth _auth;
    readonly SnapshotStore _store;
    readonly string _title;
    readonly object _longPollGate = new();
    readonly LinkedList<CancellationTokenSource> _activeLongPolls = new();

    public ServeHttpHandler(ServeAuth auth, SnapshotStore store, string title)
    {
        _auth = auth;
        _store = store;
        _title = title;
    }

    public async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
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

            await HandleApiAsync(context, path, cancellationToken);
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

    async Task HandleApiAsync(
        HttpListenerContext context,
        string path,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.Ordinal))
        {
            WriteStatus(context, 405, "Method not allowed");
            return;
        }

        switch (path)
        {
            case "/api/state":
                await HandleStateAsync(context, cancellationToken);
                break;
            case "/api/snapshot":
                HandleSnapshot(context);
                break;
            case "/api/source":
                HandleSource(context);
                break;
            case "/api/metrics":
                // Loopback + authed view of the latest measurements (analysis time, JSON size).
                WriteJson(context, 200, ServeStateJson.Build(_store.GetState()));
                break;
            default:
                WriteStatus(context, 404, "Not found");
                break;
        }
    }

    async Task HandleStateAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var pollCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        LinkedListNode<CancellationTokenSource> pollNode;
        CancellationTokenSource? evicted = null;
        lock (_longPollGate)
        {
            if (_activeLongPolls.Count >= MaxConcurrentLongPolls)
            {
                evicted = _activeLongPolls.First!.Value;
                _activeLongPolls.RemoveFirst();
            }
            pollNode = _activeLongPolls.AddLast(pollCancellation);
        }
        try { evicted?.Cancel(); } catch (ObjectDisposedException) { }

        try
        {
            var after = ParseAfter(context.Request.QueryString["after"]);
            var response = context.Response;
            response.StatusCode = 200;
            response.ContentType = "application/json; charset=utf-8";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Cache-Control"] = "no-store";
            response.SendChunked = true;

            var waitTask = _store.WaitForChangeAsync(
                after, LongPollTimeout, pollCancellation.Token);
            try
            {
                while (!waitTask.IsCompleted)
                {
                    var heartbeat = Task.Delay(LongPollHeartbeat, pollCancellation.Token);
                    if (await Task.WhenAny(waitTask, heartbeat) == waitTask)
                        break;

                    await response.OutputStream.WriteAsync(
                        JsonWhitespace, pollCancellation.Token);
                    await response.OutputStream.FlushAsync(pollCancellation.Token);
                }

                var state = await waitTask;
                var body = Encoding.UTF8.GetBytes(ServeStateJson.Build(state));
                await response.OutputStream.WriteAsync(body, cancellationToken);
                response.OutputStream.Close();
            }
            catch (Exception ex) when (ex is IOException
                or HttpListenerException
                or ObjectDisposedException)
            {
                pollCancellation.Cancel();
                try { await waitTask; } catch (OperationCanceledException) { }
                throw;
            }
        }
        finally
        {
            lock (_longPollGate)
            {
                if (pollNode.List is not null)
                    _activeLongPolls.Remove(pollNode);
            }
            pollCancellation.Dispose();
        }
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

    void HandleSource(HttpListenerContext context)
    {
        var snapshot = _store.Current;
        if (snapshot is null)
        {
            WriteStatus(context, 503, "No snapshot yet");
            return;
        }

        var fileId = context.Request.QueryString["fileId"];
        if (string.IsNullOrEmpty(fileId))
        {
            WriteStatus(context, 400, "Missing fileId");
            return;
        }

        // Exact-match allowlist only: the API never accepts a raw path, and a fileId not in
        // the current snapshot's allowlist is rejected (no StartsWith, no path traversal).
        if (!snapshot.Content.FileIdToAbsolutePath.TryGetValue(fileId, out var absolutePath))
        {
            WriteStatus(context, 404, "Unknown fileId");
            return;
        }

        if (!SourcePathBoundary.TryResolveAllowedFile(
                absolutePath,
                snapshot.Content.AllowedSourceRoots,
                out var resolvedPath)
            || !SourcePathBoundary.PathComparer.Equals(resolvedPath, absolutePath))
        {
            WriteStatus(context, 404, "Source unavailable");
            return;
        }

        string text;
        try
        {
            text = File.ReadAllText(resolvedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            WriteStatus(context, 404, "Source unavailable");
            return;
        }

        var displayPath = snapshot.Content.FileIdToDisplayPath.GetValueOrDefault(fileId, fileId);
        var body = Encoding.UTF8.GetBytes(text);
        var response = context.Response;
        response.StatusCode = 200;
        response.ContentType = "text/plain; charset=utf-8";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Cache-Control"] = "no-store";
        // Relative display name only; the absolute path never reaches the client.
        response.Headers["X-Unilyze-Source-Path"] = displayPath;
        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body, 0, body.Length);
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
