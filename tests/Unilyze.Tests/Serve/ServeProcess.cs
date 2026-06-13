using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Unilyze.Tests.Serve;

/// <summary>
/// Launches the built CLI as a real <c>unilyze serve</c> process (mirroring the CLI/MCP
/// process-level E2E suites), waits for the loopback URL it prints to stderr, and exposes
/// helpers to drive the HTTP API. Disposed at the end of each test, killing the tree.
/// </summary>
internal sealed class ServeProcess : IDisposable
{
    static readonly string CurrentTargetFramework = ResolveCurrentTargetFramework();
    static readonly string DotnetHostPath = ResolveDotnetHostPath();
    static readonly string AppDllPath = ResolveAppDllPath();

    readonly Process _process;
    readonly List<string> _stderr;

    public string BaseUrl { get; }

    public HttpClient Client { get; } = new() { Timeout = TimeSpan.FromSeconds(40) };

    ServeProcess(Process process, string baseUrl, List<string> stderr)
    {
        _process = process;
        BaseUrl = baseUrl;
        _stderr = stderr;
    }

    public static ServeProcess Start(string projectDir, params string[] extraArgs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = DotnetHostPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(AppDllPath);
        psi.ArgumentList.Add("serve");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(projectDir);
        psi.ArgumentList.Add("--no-open");
        foreach (var arg in extraArgs)
            psi.ArgumentList.Add(arg);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start serve process");

        var stderr = new List<string>();
        var urlSignal = new ManualResetEventSlim(false);
        string? url = null;

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderr) stderr.Add(e.Data);
            var match = Regex.Match(e.Data, @"listening on (http://127\.0\.0\.1:\d+/)");
            if (match.Success)
            {
                url = match.Groups[1].Value;
                urlSignal.Set();
            }
        };
        process.BeginErrorReadLine();
        // Drain stdout so the pipe never blocks the child.
        process.OutputDataReceived += (_, _) => { };
        process.BeginOutputReadLine();

        if (!urlSignal.Wait(TimeSpan.FromSeconds(30)) || url is null)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new InvalidOperationException(
                "serve did not print a URL within 30s. stderr:\n" + string.Join('\n', stderr));
        }

        return new ServeProcess(process, url, stderr);
    }

    public string StdErr()
    {
        lock (_stderr) return string.Join('\n', _stderr);
    }

    public void Dispose()
    {
        try { _process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        try { _process.WaitForExit(5000); } catch { /* ignore */ }
        _process.Dispose();
        Client.Dispose();
    }

    static string ResolveCurrentTargetFramework()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var tfm = Path.GetFileName(baseDir);
        if (string.IsNullOrWhiteSpace(tfm) || !tfm.StartsWith("net", StringComparison.Ordinal))
            throw new InvalidOperationException($"Could not infer target framework from: {AppContext.BaseDirectory}");
        return tfm;
    }

    static string ResolveDotnetHostPath()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return string.IsNullOrWhiteSpace(configured) ? "dotnet" : configured;
    }

    static string ResolveAppDllPath()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Unilyze", "bin", "Debug", CurrentTargetFramework, "Unilyze.dll"));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Could not find CLI assembly under test: {path}", path);
        return path;
    }
}
