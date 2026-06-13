using System.Net;
using Unilyze.Cli;

namespace Unilyze.Serve;

/// <summary>
/// Owns the lifetime of a <c>unilyze serve</c> session: an HTTP server resident on
/// loopback that re-analyzes on source change and serves the live viewer. Unlike the
/// one-shot analyze pipeline and the stdin-EOF MCP loop, this process runs until it
/// receives Ctrl-C / SIGTERM, at which point it shuts the listener down and exits 0.
/// </summary>
internal sealed class ServeApp
{
    readonly ServeOptions _options;

    public ServeApp(ServeOptions options) => _options = options;

    public int Run()
    {
        using var shutdown = new CancellationTokenSource();
        RegisterShutdownSignals(shutdown);

        using var server = new ServeHttpServer(IPAddress.Loopback, _options.Port);
        server.Start();
        var url = server.Url;

        var auth = new ServeAuth(server.Port);
        var handler = new ServeHttpHandler(auth);

        Console.Error.WriteLine($"unilyze serve listening on {url}");
        Console.Error.WriteLine("Press Ctrl-C to stop.");

        if (!_options.NoOpen)
            ProgramHelpers.TryOpenInBrowser(url);

        using var acceptLoop = server.RunAcceptLoop(handler.Handle, shutdown.Token);

        shutdown.Token.WaitHandle.WaitOne();
        Console.Error.WriteLine("Shutting down...");
        return 0;
    }

    // (request handling now lives in ServeHttpHandler)

    void RegisterShutdownSignals(CancellationTokenSource shutdown)
    {
        Console.CancelKeyPress += (_, e) =>
        {
            // Prevent the runtime's default hard-kill so the listener can drain.
            e.Cancel = true;
            if (!shutdown.IsCancellationRequested)
                shutdown.Cancel();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (!shutdown.IsCancellationRequested)
                shutdown.Cancel();
        };
    }

    // Request handling lives in ServeHttpHandler.
}
