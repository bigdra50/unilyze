using System.Net;

namespace Unilyze.Serve;

/// <summary>
/// A loopback-only <see cref="HttpListener"/> wrapper. LAN exposure is intentionally
/// out of scope, so the bind address is always <see cref="IPAddress.Loopback"/> and
/// there is no <c>--host</c>. Because <see cref="HttpListener"/> exposes no API to read
/// back an OS-assigned port (so port 0 is unusable), we either bind the requested
/// <c>--port</c> or probe random high ports and retry on conflict.
/// </summary>
internal sealed class ServeHttpServer : IDisposable
{
    const int MinEphemeralPort = 49152;
    const int MaxEphemeralPort = 65535;
    const int RandomPortAttempts = 25;

    readonly IPAddress _address;
    readonly int? _requestedPort;
    HttpListener? _listener;

    public ServeHttpServer(IPAddress address, int? requestedPort)
    {
        _address = address;
        _requestedPort = requestedPort;
    }

    public int Port { get; private set; }

    public string Url => $"http://{_address}:{Port}/";

    public void Start()
    {
        if (_requestedPort is { } fixedPort)
        {
            try
            {
                _listener = BindOrThrow(fixedPort);
            }
            catch (HttpListenerException ex)
            {
                throw new InvalidOperationException(
                    $"Could not bind 127.0.0.1:{fixedPort} ({ex.Message}). "
                    + "The port may be in use; omit --port to auto-select one.", ex);
            }
            Port = fixedPort;
            return;
        }

        var rng = new Random();
        HttpListenerException? lastError = null;
        for (var attempt = 0; attempt < RandomPortAttempts; attempt++)
        {
            var candidate = rng.Next(MinEphemeralPort, MaxEphemeralPort + 1);
            try
            {
                _listener = BindOrThrow(candidate);
                Port = candidate;
                return;
            }
            catch (HttpListenerException ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            $"Could not bind a loopback port after {RandomPortAttempts} attempts. "
            + "Pass --port to choose one explicitly.", lastError);
    }

    HttpListener BindOrThrow(int port)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://{_address}:{port}/");
        try
        {
            listener.Start();
        }
        catch
        {
            listener.Close();
            throw;
        }
        return listener;
    }

    /// <summary>
    /// Spins up a background accept loop. Each request is dispatched to the thread pool
    /// so a blocking long-poll handler cannot starve the listener. The returned token
    /// registration stops the listener on cancellation, unblocking the loop.
    /// </summary>
    public IDisposable RunAcceptLoop(Action<HttpListenerContext> handler, CancellationToken token)
    {
        var listener = _listener ?? throw new InvalidOperationException("Server not started.");
        var registration = token.Register(Stop);

        var thread = new Thread(() => AcceptLoop(listener, handler, token))
        {
            IsBackground = true,
            Name = "unilyze-serve-accept",
        };
        thread.Start();

        return registration;
    }

    static void AcceptLoop(HttpListener listener, Action<HttpListenerContext> handler, CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = listener.GetContext();
            }
            catch (Exception) when (token.IsCancellationRequested || !listener.IsListening)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(_ => DispatchSafely(handler, context));
        }
    }

    static void DispatchSafely(Action<HttpListenerContext> handler, HttpListenerContext context)
    {
        try
        {
            handler(context);
        }
        catch (Exception)
        {
            TryAbort(context);
        }
    }

    static void TryAbort(HttpListenerContext context)
    {
        try
        {
            context.Response.Abort();
        }
        catch
        {
            // The client may already be gone; nothing more to do.
        }
    }

    void Stop()
    {
        try
        {
            _listener?.Stop();
        }
        catch
        {
            // Already stopped/disposed.
        }
    }

    public void Dispose()
    {
        try
        {
            _listener?.Close();
        }
        catch
        {
            // Best-effort teardown.
        }
    }
}
