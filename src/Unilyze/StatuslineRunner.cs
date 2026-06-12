using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Unilyze;

internal static class StatuslineRunner
{
    const string CachePrefix = "unilyze-sl-";
    const int DefaultRefreshSeconds = 60;

    sealed record StatuslineRequest(
        bool Verbose,
        bool Quiet,
        bool BackgroundRefresh,
        string Path,
        int RefreshSeconds,
        string? BaselinePath,
        AnalysisLevel? RequestedLevel)
    {
        public ConsoleAnalysisLogSink CreateLogSink() => new(quiet: Quiet);
    }

    sealed record StatuslineCachePaths(string TxtPath, string LockPath);

    public static int Run(string[] args)
    {
        if (CliArgValidationSupport.IsHelpRequest(args))
            return PrintUsage();

        var usageError = CliArgValidation.ValidateStatuslineArgs(args);
        if (usageError != 0)
            return usageError;

        if (!TryParseRequest(args, out var request))
            return 1;

        if (!TryResolveFullPath(request.Path, request.Verbose, out var fullPath))
            return 1;

        var paths = CreateCachePaths(fullPath);

        return request.BackgroundRefresh
            ? RunBackgroundRefresh(fullPath, request, paths.TxtPath)
            : RunForeground(fullPath, request, paths);
    }

    static bool TryParseRequest(string[] args, out StatuslineRequest request)
    {
        var opts = ProgramHelpers.ParseOptions(args);

        var verbose = opts.ContainsKey("--verbose");
        var quiet = opts.ContainsKey("--quiet");
        var backgroundRefresh = opts.ContainsKey("--background-refresh");

        var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
        var refreshStr = opts.GetValueOrDefault("--refresh") ?? DefaultRefreshSeconds.ToString();
        var baselinePath = opts.GetValueOrDefault("--baseline");
        if (!int.TryParse(refreshStr, out var refreshSeconds))
            refreshSeconds = DefaultRefreshSeconds;

        if (ProgramHelpers.HasFlagWithoutValue(args, "--baseline"))
        {
            Console.Error.WriteLine("--baseline requires a file path.");
            request = null!;
            return false;
        }

        if (!TryParseRequestedLevel(opts.GetValueOrDefault("--level"), out var requestedLevel))
        {
            request = null!;
            return false;
        }

        request = new StatuslineRequest(
            verbose,
            quiet,
            backgroundRefresh,
            path,
            refreshSeconds,
            baselinePath,
            requestedLevel);
        return true;
    }

    static bool TryResolveFullPath(string path, bool verbose, out string fullPath)
    {
        try
        {
            fullPath = ProgramHelpers.ResolveProjectRoot(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            if (verbose)
                PrintVerboseException(ex);
            fullPath = null!;
            return false;
        }
    }

    static StatuslineCachePaths CreateCachePaths(string fullPath)
    {
        var cacheHash = ComputePathHash(fullPath);
        var cacheDir = Path.GetTempPath();
        return new StatuslineCachePaths(
            Path.Combine(cacheDir, $"{CachePrefix}{cacheHash}.txt"),
            Path.Combine(cacheDir, $"{CachePrefix}{cacheHash}.lock"));
    }

    static int RunForeground(string fullPath, StatuslineRequest request, StatuslineCachePaths paths)
    {
        if (TryServeFreshCache(paths.TxtPath, request.RefreshSeconds))
            return 0;

        FileStream? lockStream;
        try
        {
            lockStream = new FileStream(paths.LockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        }
        catch (IOException)
        {
            return ServeStaleCacheOr(1, paths.TxtPath);
        }

        var log = request.CreateLogSink();
        try
        {
            return RunAnalysisAndServe(fullPath, request, log, paths.TxtPath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or DirectoryNotFoundException or JsonException)
        {
            if (request.Verbose)
                PrintVerboseException(ex);
            return ServeStaleCacheOr(
                1,
                paths.TxtPath,
                verboseNote: request.Verbose ? "Serving stale cache after analysis failure." : null);
        }
        finally
        {
            lockStream.Dispose();
            TryDeleteLockFile(paths.LockPath);
        }
    }

    static void TryDeleteLockFile(string lockPath)
    {
        try
        {
            File.Delete(lockPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }

    static bool TryParseRequestedLevel(string? levelStr, out AnalysisLevel? requestedLevel)
    {
        requestedLevel = null;
        if (levelStr == null)
            return true;

        if (!AnalysisLevelOption.TryParse(levelStr, out var lvl))
        {
            Console.Error.WriteLine($"Unknown level: '{levelStr}'. Valid levels: syntax, core, full, complete");
            return false;
        }

        requestedLevel = lvl;
        return true;
    }

    static void PrintVerboseException(Exception ex)
    {
        Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
        if (ex.StackTrace is not null)
            Console.Error.WriteLine(ex.StackTrace);
    }

    static int RunBackgroundRefresh(string fullPath, StatuslineRequest request, string cacheTxtPath)
    {
        if (TryServeFreshCache(cacheTxtPath, request.RefreshSeconds))
            return 0;

        if (File.Exists(cacheTxtPath))
            Console.Write(File.ReadAllText(cacheTxtPath));

        TrySpawnBackgroundRefresh(fullPath, request);
        return 0;
    }

    static void TrySpawnBackgroundRefresh(string fullPath, StatuslineRequest request)
    {
        var childArgs = BuildBackgroundRefreshArgs(fullPath, request);
        var (host, args) = ResolveSelfInvocation(childArgs);

        try
        {
            var proc = StartDetachedProcess(host, args);
            if (proc is null)
                return;

            DrainProcessInBackground(proc);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            // Best-effort background refresh; foreground caller must never block.
        }
    }

    static List<string> BuildBackgroundRefreshArgs(string fullPath, StatuslineRequest request)
    {
        var childArgs = new List<string>
        {
            "statusline",
            "-p",
            fullPath,
            "--refresh",
            request.RefreshSeconds.ToString(),
        };

        if (request.RequestedLevel is { } level)
        {
            childArgs.Add("--level");
            childArgs.Add(LevelToCliToken(level));
        }

        if (request.BaselinePath is not null)
        {
            childArgs.Add("--baseline");
            childArgs.Add(request.BaselinePath);
        }

        return childArgs;
    }

    static Process? StartDetachedProcess(string host, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = host,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        return Process.Start(psi);
    }

    static void DrainProcessInBackground(Process proc)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await proc.StandardOutput.ReadToEndAsync();
                await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();
            }
            finally
            {
                proc.Dispose();
            }
        });
    }

    static (string Host, IReadOnlyList<string> Args) ResolveSelfInvocation(IReadOnlyList<string> args)
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) && !IsDotnetHost(processPath))
            return (processPath, args);

        return ResolveDotnetSelfInvocation(processPath, args);
    }

    static (string Host, IReadOnlyList<string> Args) ResolveDotnetSelfInvocation(
        string? processPath,
        IReadOnlyList<string> args)
    {
        var entryAssembly = Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrEmpty(entryAssembly))
            entryAssembly = typeof(StatuslineRunner).Assembly.Location;

        var dotnetHost = string.IsNullOrEmpty(processPath) || IsDotnetHost(processPath)
            ? "dotnet"
            : processPath;

        var dotnetArgs = new List<string> { entryAssembly };
        dotnetArgs.AddRange(args);
        return (dotnetHost, dotnetArgs);
    }

    static bool IsDotnetHost(string path) =>
        Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    static string LevelToCliToken(AnalysisLevel level) =>
        level switch
        {
            AnalysisLevel.Syntax => "syntax",
            AnalysisLevel.Core => "core",
            AnalysisLevel.Full => "full",
            AnalysisLevel.Complete => "complete",
            _ => "complete",
        };

    static bool TryServeFreshCache(string cacheTxtPath, int refreshSeconds)
    {
        if (!File.Exists(cacheTxtPath))
            return false;

        var cacheAge = DateTimeOffset.UtcNow - new DateTimeOffset(File.GetLastWriteTimeUtc(cacheTxtPath));
        if (cacheAge.TotalSeconds >= refreshSeconds)
            return false;

        Console.Write(File.ReadAllText(cacheTxtPath));
        return true;
    }

    static int ServeStaleCacheOr(int fallbackExit, string cacheTxtPath, string? verboseNote = null)
    {
        if (!File.Exists(cacheTxtPath))
            return fallbackExit;

        if (verboseNote is not null)
            Console.Error.WriteLine(verboseNote);
        Console.Write(File.ReadAllText(cacheTxtPath));
        return 0;
    }

    static int RunAnalysisAndServe(
        string fullPath,
        StatuslineRequest request,
        ConsoleAnalysisLogSink log,
        string cacheTxtPath)
    {
        var built = BuildAnalysisResult(fullPath, request, log);
        if (built is null)
            return 1;

        var formatted = FormatStatusline(built.Value.Result, built.Value.ExcludeBaselined);
        File.WriteAllText(cacheTxtPath, formatted);
        Console.Write(formatted);
        return 0;
    }

    static (AnalysisResult Result, bool ExcludeBaselined)? BuildAnalysisResult(
        string fullPath,
        StatuslineRequest request,
        ConsoleAnalysisLogSink log)
    {
        var config = UnilyzeConfig.LoadMerged(fullPath);
        var resolved = config.ResolveAnalysisConfig();
        var result = AnalysisPipeline.Build(
            fullPath, null, null, config.ExcludeDirs, request.RequestedLevel,
            excludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
            applyAnyDepthExcludes: !config.DisableDefaultExcludes,
            logSink: log,
            analysisConfig: resolved,
            maxParallelism: config.MaxParallelism);

        var effectiveBaseline = request.BaselinePath ?? config.Baseline;
        var baselineError = ProgramHelpers.TryApplyBaseline(result, fullPath, effectiveBaseline, out result);
        if (baselineError is 1)
            return null;

        var triagePath = TriageApplication.ResolvePath(new Dictionary<string, string>(), config, fullPath);
        var triageError = TriageApplication.TryApply(result, triagePath, out result);
        return triageError is 1 ? null : (result, effectiveBaseline is not null);
    }

    static string FormatStatusline(AnalysisResult result, bool excludeBaselined)
    {
        var summary = StatuslineFormatter.ComputeSummary(result, excludeBaselined);
        return StatuslineFormatter.Format(summary);
    }

    static string ComputePathHash(string path)
    {
        #pragma warning disable CA5351 // cache key only, not security
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(path));
        #pragma warning restore CA5351
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static string BuildUsageText()
    {
        var tempDir = Path.GetTempPath();
        return $$"""
            {{BuildUsageHeader()}}

            {{BuildUsageOptionsSection()}}

            {{BuildUsageProgressSection()}}

            {{BuildUsageOutputSection()}}

            {{BuildUsageColorSection()}}

            {{BuildUsageCacheSection(tempDir)}}
            """;
    }

    static string BuildUsageHeader() =>
        """
        unilyze statusline - Output compact code health for status line display

        Usage:
          unilyze statusline                         Analyze current directory
          unilyze statusline -p <path>               Analyze specified project
          unilyze statusline -p <path> --refresh 30  Custom cache interval (seconds)
        """;

    static string BuildUsageOptionsSection() =>
        """
        Options:
          -p, --path     Project root (default: .)
          --refresh      Cache refresh interval in seconds (default: 60)
          --level        Pin analysis level: syntax, core, full, complete
          --baseline     Suppress known smells from a baseline file in smell counts
          --verbose      Print diagnostics (swallowed exceptions, stale-cache notes) to stderr
          --quiet        Suppress info lines on stderr (warnings still shown)
          --background-refresh
                         Never block on analysis: return cached output immediately and refresh
                         stale or missing caches in a detached background process
          -h, --help     Show this help
        """;

    static string BuildUsageProgressSection() =>
        """
        Progress:
          Per-phase progress is shown on stderr when stderr is a TTY.
        """;

    static string BuildUsageOutputSection() =>
        """
        Output format: CH:9.4/3.2 MI:72 87smells 🔴5 📦12 ♻3 [core]
          CH:<avg>/<min> = Code Health average and minimum (1.0-10.0), always shown
          MI:<n>         = Average Maintainability Index (integer), always shown
          <n>smells      = Warning code smells count, always shown
          🔴<n>          = Critical code smells count (hidden if 0)
          📦<n>          = Boxing allocation count (hidden if 0)
          ♻<n>           = Cyclic dependency count (hidden if 0)
          [level]        = Analysis level marker, shown only below Complete
                           ([syntax] / [core] / [full])
        """;

    static string BuildUsageColorSection() =>
        """
        Color coding:
          Code Health (avg and min): green (>=8.0), yellow (>=5.0), red (<5.0)
          Maintainability Index: green (>=80), yellow (>=60), red (<60)
          Warnings (smells): yellow
          Criticals: red
          Boxing: cyan
          Cyclic dependencies: red
          Level marker: yellow
        """;

    static string BuildUsageCacheSection(string tempDir) =>
        $$"""
        Cache:
          Results are cached in {{tempDir}}unilyze-sl-{hash}.txt
          Use --refresh to control cache lifetime (default: 60 seconds)
          With --background-refresh, a missing cache prints nothing (one empty status line
          render) and exits immediately while analysis runs in the background
        """;

    static int PrintUsage()
    {
        Console.WriteLine(BuildUsageText());
        return 0;
    }
}
