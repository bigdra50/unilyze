using System.Net;
using System.Text;
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

        Console.Error.WriteLine($"unilyze serve listening on {url}");
        Console.Error.WriteLine("Press Ctrl-C to stop.");

        if (!_options.NoOpen)
            ProgramHelpers.TryOpenInBrowser(url);

        using var acceptLoop = server.RunAcceptLoop(HandleRequest, shutdown.Token);

        shutdown.Token.WaitHandle.WaitOne();
        Console.Error.WriteLine("Shutting down...");
        return 0;
    }

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

    void HandleRequest(HttpListenerContext context)
    {
        // Placeholder response until the viewer + API endpoints land in later issues.
        var body = Encoding.UTF8.GetBytes("unilyze serve is starting.\n");
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        context.Response.OutputStream.Write(body, 0, body.Length);
        context.Response.OutputStream.Close();
    }
}
