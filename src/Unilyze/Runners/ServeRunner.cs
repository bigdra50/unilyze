using Unilyze.Cli;
using Unilyze.Discovery;
using Unilyze.Pipeline;
using Unilyze.Serve;

namespace Unilyze.Runners;

internal static class ServeRunner
{
    public static int Run(string[] args)
    {
        if (CliArgValidationSupport.IsHelpRequest(args))
            return PrintUsage();

        var usageError = CliArgValidation.ValidateServeArgs(args);
        if (usageError != 0)
            return usageError;

        var opts = ProgramHelpers.ParseOptions(args);

        AnalysisLevel? requestedLevel = null;
        var levelStr = opts.GetValueOrDefault("--level");
        if (levelStr != null)
        {
            if (!AnalysisLevelOption.TryParse(levelStr, out var lvl))
            {
                Console.Error.WriteLine($"Unknown level: '{levelStr}'. Valid levels: syntax, core, full, complete");
                return 1;
            }
            requestedLevel = lvl;
        }

        int? port = null;
        var portStr = opts.GetValueOrDefault("--port");
        if (portStr != null)
        {
            if (!int.TryParse(portStr, out var parsedPort) || parsedPort is < 1 or > 65535)
            {
                Console.Error.WriteLine($"Invalid --port: '{portStr}'. Expected an integer from 1 to 65535.");
                return 1;
            }
            port = parsedPort;
        }

        int? verifyIncrementalEveryN = null;
        var verifyStr = opts.GetValueOrDefault("--verify-incremental");
        if (verifyStr != null)
        {
            if (!int.TryParse(verifyStr, out var parsedVerify) || parsedVerify < 1)
            {
                Console.Error.WriteLine(
                    $"Invalid --verify-incremental: '{verifyStr}'. Expected a positive integer.");
                return 1;
            }
            verifyIncrementalEveryN = parsedVerify;
        }

        var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
        var options = new ServeOptions(
            Path: path,
            Port: port,
            NoOpen: opts.ContainsKey("--no-open"),
            RequestedLevel: requestedLevel,
            Profile: opts.GetValueOrDefault("--profile"),
            ExcludeDirs: ProgramHelpers.ParseMultiValueOption(args, "--exclude-dir"),
            Prefix: opts.GetValueOrDefault("--prefix"),
            Assembly: opts.GetValueOrDefault("-a") ?? opts.GetValueOrDefault("--assembly"),
            ResolveNuget: opts.ContainsKey("--resolve-nuget"),
            IncludeGenerated: opts.ContainsKey("--include-generated"),
            TargetFramework: opts.GetValueOrDefault("--tfm"),
            VerifyIncrementalEveryN: verifyIncrementalEveryN);

        try
        {
            return new ServeApp(options).Run();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static int PrintUsage()
    {
        Console.WriteLine("""
            unilyze serve - Live code-quality viewer over a loopback HTTP server

            Usage:
              unilyze serve -p <path>            Analyze and serve the live viewer (opens a browser)
              unilyze serve -p <path> --no-open  Serve without launching a browser (prints the URL)
              unilyze serve -p <path> --port 8765  Bind a specific loopback port

            The server stays resident, re-analyzes on source change, and pushes the full
            result to the browser without a page reload. It binds 127.0.0.1 only (no LAN
            exposure). Press Ctrl-C to stop.

            Options:
              -p, --path         Project root or Assets directory (default: .)
                  --port         Loopback port (default: random high port)
                  --no-open      Do not open a browser; print the URL to stderr
                  --level        Pin analysis level: syntax, core, full, complete
                  --profile      Built-in smell threshold profile (default; unity)
                  --exclude-dir  Exclude directory from analysis (repeatable)
              -a, --assembly     Filter by assembly name
                  --prefix       Filter asmdef names by prefix
                  --resolve-nuget  Resolve NuGet compile-time assemblies
                  --include-generated  Include generated sources in compilation
                  --tfm          Target framework for NuGet/generated-source resolution
                  --verify-incremental <N>  Every N generations, run a full analysis alongside
                                 the incremental one and log any divergence to stderr (off by
                                 default; roughly doubles cost on the sampled generations)
              -h, --help         Show this help

            Exit codes:
              0  Clean shutdown (Ctrl-C)
              1  Usage error or fatal startup failure
            """);
        return 0;
    }
}
