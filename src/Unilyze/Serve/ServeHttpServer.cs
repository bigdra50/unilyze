using System.Net;

namespace Unilyze.Serve;

internal sealed class ServeHttpServer : IDisposable
{
    const int MinEphemeralPort = 49152;
    const int MaxEphemeralPort = 65535;
    const int RandomPortAttempts = 25;

    readonly IPAddress _address;
    readonly int? _requestedPort;
    HttpListener? _listener;
    CancellationTokenSource? _requestShutdown;
    Task? _acceptTask;

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

    public IDisposable RunAcceptLoop(
        Func<HttpListenerContext, CancellationToken, Task> handler,
        CancellationToken token)
    {
        var listener = _listener ?? throw new InvalidOperationException("Server not started.");
        _requestShutdown = CancellationTokenSource.CreateLinkedTokenSource(token);
        var registration = token.Register(Stop);
        _acceptTask = AcceptLoopAsync(listener, handler, _requestShutdown.Token);
        return new AcceptLoopLifetime(this, registration);
    }

    static async Task AcceptLoopAsync(
        HttpListener listener,
        Func<HttpListenerContext, CancellationToken, Task> handler,
        CancellationToken token)
    {
        var requests = new HashSet<Task>();
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (token.IsCancellationRequested || !listener.IsListening)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            var request = DispatchSafelyAsync(handler, context, token);
            lock (requests)
            {
                requests.RemoveWhere(task => task.IsCompleted);
                requests.Add(request);
            }
        }

        Task[] pending;
        lock (requests)
            pending = requests.ToArray();
        await Task.WhenAll(pending);
    }

    static async Task DispatchSafelyAsync(
        Func<HttpListenerContext, CancellationToken, Task> handler,
        HttpListenerContext context,
        CancellationToken token)
    {
        try
        {
            await handler(context, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            TryAbort(context);
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
        }
    }

    public void Dispose()
    {
        _requestShutdown?.Cancel();
        try
        {
            _listener?.Close();
        }
        catch
        {
        }
        try
        {
            _acceptTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
        }
        _requestShutdown?.Dispose();
    }

    sealed class AcceptLoopLifetime : IDisposable
    {
        readonly ServeHttpServer _server;
        readonly CancellationTokenRegistration _registration;
        int _disposed;

        public AcceptLoopLifetime(
            ServeHttpServer server,
            CancellationTokenRegistration registration)
        {
            _server = server;
            _registration = registration;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _registration.Dispose();
            _server._requestShutdown?.Cancel();
            _server.Stop();
            try
            {
                _server._acceptTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
            }
        }
    }
}
