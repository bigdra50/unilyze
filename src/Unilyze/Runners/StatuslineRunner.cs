using Unilyze.Findings;
using Unilyze.Discovery;
using Unilyze.Output;
using Unilyze.Config;
using Unilyze.Cli;
using Unilyze.Pipeline;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Unilyze.Runners;

internal static class StatuslineRunner
{
    const string CachePrefix = "unilyze-sl-";
    const int DefaultRefreshSeconds = 60;

    // A refresh lock older than this is assumed abandoned (crashed holder) and reclaimable.
    // Comfortably above a normal cold analysis (~a few seconds) so a live refresh is never stolen.
    const int LockStaleSeconds = 120;

    sealed record StatuslineRequest(
        bool Verbose,
        bool Quiet,
        bool RefreshNow,
        bool Incremental,
        bool UseCodeHealthV1,
        bool ShowMi,
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

        var paths = CreateCachePaths(fullPath, request.UseCodeHealthV1, request.ShowMi);

        // Default is stale-while-revalidate: never block a foreground caller on analysis.
        // The detached child re-enters here with --refresh-now to run the synchronous path.
        return request.RefreshNow
            ? RunRefreshNow(fullPath, request, paths)
            : RunStaleWhileRevalidate(fullPath, request, paths);
    }

    static bool TryParseRequest(string[] args, out StatuslineRequest request)
    {
        var opts = ProgramHelpers.ParseOptions(args);

        var verbose = opts.ContainsKey("--verbose");
        var quiet = opts.ContainsKey("--quiet");
        // --refresh-now is the internal synchronous entry point the detached child runs.
        // --background-refresh is retained as an accepted no-op: non-blocking is now the default.
        var refreshNow = opts.ContainsKey("--refresh-now");
        var incremental = opts.ContainsKey("--incremental");
        var useCodeHealthV1 = opts.ContainsKey("--codehealth-v1");
        var showMi = opts.ContainsKey("--show-mi");

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
            refreshNow,
            incremental,
            useCodeHealthV1,
            showMi,
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

    static StatuslineCachePaths CreateCachePaths(string fullPath, bool useCodeHealthV1, bool showMi)
    {
        var cacheKey = fullPath;
        if (useCodeHealthV1)
            cacheKey += "\0codehealth-v1";
        if (showMi)
            cacheKey += "\0show-mi";
        var cacheHash = ComputePathHash(cacheKey);
        var cacheDir = Path.GetTempPath();
        return new StatuslineCachePaths(
            Path.Combine(cacheDir, $"{CachePrefix}{cacheHash}.txt"),
            Path.Combine(cacheDir, $"{CachePrefix}{cacheHash}.lock"));
    }

    // Stale-while-revalidate: the default path. Serves the cache (any age) without
    // ever running analysis in the foreground, and schedules a detached refresh when
    // the cache is stale or missing. A tight-budget consumer (e.g. a status bar that
    // kills children after ~250ms) always gets an immediate answer.
    static int RunStaleWhileRevalidate(string fullPath, StatuslineRequest request, StatuslineCachePaths paths)
    {
        var cacheExists = File.Exists(paths.TxtPath);
        double? ageSeconds = cacheExists
            ? (DateTimeOffset.UtcNow - new DateTimeOffset(File.GetLastWriteTimeUtc(paths.TxtPath))).TotalSeconds
            : null;

        var action = StatuslineCacheDecision.Decide(cacheExists, ageSeconds, request.RefreshSeconds);
        switch (action)
        {
            case StatuslineCacheAction.ServeFresh:
                Console.Write(TryReadCache(paths.TxtPath));
                return 0;

            case StatuslineCacheAction.ServeStaleAndRefresh:
                Console.Write(TryReadCache(paths.TxtPath));
                TrySpawnBackgroundRefresh(fullPath, request);
                return 0;

            case StatuslineCacheAction.RefreshOnly:
            default:
                // No cache yet: print nothing (hidden segment) and warm it in the background.
                TrySpawnBackgroundRefresh(fullPath, request);
                return 0;
        }
    }

    static string TryReadCache(string cacheTxtPath)
    {
        try
        {
            return File.ReadAllText(cacheTxtPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cache vanished or was mid-rename; a status bar prefers empty over a crash.
            return "";
        }
    }

    // Synchronous analysis path, run only by the detached refresh child (or an explicit
    // --refresh-now caller). Holds a lock so concurrent invocations do not stampede,
    // then atomically rewrites the cache.
    static int RunRefreshNow(string fullPath, StatuslineRequest request, StatuslineCachePaths paths)
    {
        if (!TryAcquireRefreshLock(paths.LockPath, out var lockStream))
            // Another refresh already owns the lock; nothing to do.
            return 0;

        var log = request.CreateLogSink();
        try
        {
            var built = BuildAnalysisResult(fullPath, request, log);
            if (built is null)
                return 1;

            var formatted = FormatStatusline(
                built.Value.Result,
                built.Value.ExcludeBaselined,
                request.UseCodeHealthV1,
                request.ShowMi);
            AtomicWriteCache(paths.TxtPath, formatted);
            TryWriteStdout(formatted);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or DirectoryNotFoundException or JsonException)
        {
            if (request.Verbose)
                PrintVerboseException(ex);
            return 1;
        }
        finally
        {
            ReleaseRefreshLock(lockStream, paths.LockPath);
        }
    }

    // Acquires the refresh lock by creating the lock file exclusively (atomic across
    // processes). A lock older than LockStaleSeconds is assumed abandoned and reclaimed.
    static bool TryAcquireRefreshLock(string lockPath, out FileStream? lockStream)
    {
        lockStream = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                lockStream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var stamp = Encoding.UTF8.GetBytes($"{Environment.ProcessId} {DateTimeOffset.UtcNow:O}\n");
                lockStream.Write(stamp, 0, stamp.Length);
                lockStream.Flush();
                return true;
            }
            catch (IOException)
            {
                // Lock exists. Reclaim it once if it is stale, otherwise give up.
                if (attempt == 0 && TryReclaimStaleLock(lockPath))
                    continue;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
        return false;
    }

    static bool TryReclaimStaleLock(string lockPath)
    {
        try
        {
            var age = DateTimeOffset.UtcNow - new DateTimeOffset(File.GetLastWriteTimeUtc(lockPath));
            if (age.TotalSeconds < LockStaleSeconds)
                return false;
            File.Delete(lockPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A live holder keeps the file open on Windows; deletion fails -> not reclaimable.
            return false;
        }
    }

    static void ReleaseRefreshLock(FileStream? lockStream, string lockPath)
    {
        try
        {
            lockStream?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // best-effort
        }

        try
        {
            File.Delete(lockPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }

    // Writes to a sibling temp file then renames over the target so a concurrent reader
    // sees either the old or the new content, never a partial write. Same directory keeps
    // the rename atomic (same volume).
    static void AtomicWriteCache(string cacheTxtPath, string content)
    {
        var dir = Path.GetDirectoryName(cacheTxtPath) ?? Path.GetTempPath();
        var tmp = Path.Combine(dir, $"{Path.GetFileName(cacheTxtPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tmp, content);
        try
        {
            File.Move(tmp, cacheTxtPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* best-effort */ }
            throw;
        }
    }

    static void TryWriteStdout(string text)
    {
        try
        {
            Console.Write(text);
        }
        catch (IOException)
        {
            // Detached background child: the parent's pipe is already gone. The cache is
            // written regardless, so a broken stdout is harmless.
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

    static void TrySpawnBackgroundRefresh(string fullPath, StatuslineRequest request)
    {
        var childArgs = BuildRefreshNowArgs(fullPath, request);
        var (host, args) = ResolveSelfInvocation(childArgs);

        try
        {
            StartDetachedProcess(host, args);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            // Best-effort background refresh; foreground caller must never block or fail.
        }
    }

    static List<string> BuildRefreshNowArgs(string fullPath, StatuslineRequest request)
    {
        var childArgs = new List<string>
        {
            "statusline",
            "--refresh-now",
            "-p",
            fullPath,
            // Keep the detached child's stderr silent; its stdout is discarded anyway.
            "--quiet",
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

        if (request.Incremental)
            childArgs.Add("--incremental");

        if (request.UseCodeHealthV1)
            childArgs.Add("--codehealth-v1");

        if (request.ShowMi)
            childArgs.Add("--show-mi");

        return childArgs;
    }

    // Spawns the refresh child fully detached: its stdio is redirected to pipes we never
    // read and never wait on, so (1) it does not inherit — and thus does not hold open —
    // the consumer's stdout (which would make a status bar block until analysis finishes),
    // and (2) it outlives this process. Works the same on Windows (no shell tricks).
    static void StartDetachedProcess(string host, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = host,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var proc = Process.Start(psi);
        if (proc is null)
            return;

        // Close our end of stdin so the child sees EOF immediately, then walk away:
        // no WaitForExit, no draining. The child writes ~one short line long after we
        // exit; that write lands in a pipe nobody reads and is discarded.
        try
        {
            proc.StandardInput.Close();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // best-effort
        }
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
        // Single-file binaries never reach this method: ResolveSelfInvocation returns
        // Environment.ProcessPath directly for non-dotnet hosts, and Location is only
        // consulted when running under the dotnet host where it is populated.
        #pragma warning disable IL3000
        var entryAssembly = Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrEmpty(entryAssembly))
            entryAssembly = typeof(StatuslineRunner).Assembly.Location;
        #pragma warning restore IL3000

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

    static (AnalysisResult Result, bool ExcludeBaselined)? BuildAnalysisResult(
        string fullPath,
        StatuslineRequest request,
        ConsoleAnalysisLogSink log)
    {
        var config = UnilyzeConfig.LoadMerged(fullPath);
        var referenceSettings = ReferenceAnalysisSettings.LoadMerged(fullPath);
        var resolved = config.ResolveAnalysisConfig();
        var result = AnalysisPipeline.Build(
            fullPath, null, null, config.ExcludeDirs, request.RequestedLevel,
            excludeGeneratedCode: !config.DisableGeneratedCodeExcludes,
            applyAnyDepthExcludes: !config.DisableDefaultExcludes,
            logSink: log,
            analysisConfig: resolved,
            maxParallelism: config.MaxParallelism,
            resolveNuget: referenceSettings.ResolveNuget,
            includeGenerated: referenceSettings.IncludeGenerated,
            targetFramework: referenceSettings.TargetFramework,
            incremental: request.Incremental);

        var effectiveBaseline = request.BaselinePath ?? config.Baseline;
        var baselineError = ProgramHelpers.TryApplyBaseline(result, fullPath, effectiveBaseline, out result);
        if (baselineError is 1)
            return null;

        var triagePath = TriageApplication.ResolvePath(new Dictionary<string, string>(), config, fullPath);
        var triageError = TriageApplication.TryApply(result, triagePath, out result);
        return triageError is 1 ? null : (result, effectiveBaseline is not null);
    }

    static string FormatStatusline(
        AnalysisResult result,
        bool excludeBaselined,
        bool useCodeHealthV1,
        bool showMi)
    {
        var summary = StatuslineFormatter.ComputeSummary(result, excludeBaselined, useCodeHealthV1);
        return StatuslineFormatter.Format(summary, showMi);
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

        Never blocks: it prints the cached line immediately (nothing on a cold cache)
        and refreshes a stale or missing cache in a detached background process.

        Usage:
          unilyze statusline                         Current directory (non-blocking)
          unilyze statusline -p <path>               Specified project (non-blocking)
          unilyze statusline -p <path> --refresh 30  Custom cache interval (seconds)
        """;

    static string BuildUsageOptionsSection() =>
        """
        Options:
          -p, --path     Project root (default: .)
          --refresh      Cache refresh interval in seconds (default: 60)
          --level        Pin analysis level: syntax, core, full, complete
          --baseline     Suppress known smells from a baseline file in smell counts
          --incremental  Reuse syntax-level parse cache in <project>/.unilyze/cache/ (requires --level syntax)
          --codehealth-v1
                         Display legacy CodeHealth v1 during the one-release migration window
          --show-mi      Append the reference Maintainability Index metric to the output
          --verbose      Print diagnostics (swallowed exceptions, stale-cache notes) to stderr
          --quiet        Suppress info lines on stderr (warnings still shown)
          --background-refresh
                         Accepted for compatibility; non-blocking refresh is now the default
          -h, --help     Show this help
        """;

    static string BuildUsageProgressSection() =>
        """
        Progress:
          Per-phase progress is shown on stderr when stderr is a TTY.
        """;

    static string BuildUsageOutputSection() =>
        """
        Output format: CH:9.4/3.2 W:9.1 T:7.8 87smells 🔴5 📦12 ♻3 [core]
          CH:<avg>/<min> = Code Health average and minimum (1.0-10.0), always shown
          <n>smells      = Warning code smells count, always shown
          🔴<n>          = Critical code smells count (hidden if 0)
          📦<n>          = Boxing allocation count (hidden if 0)
          ♻<n>           = Cyclic dependency count (hidden if 0)
          [level]        = Analysis level marker, shown only below Complete
                           ([syntax] / [core] / [full])

        With --show-mi:
          MI:<n>         = Average Maintainability Index (integer reference metric)
        """;

    static string BuildUsageColorSection() =>
        """
        Color coding:
          Code Health (avg and min): green (>=8.0), yellow (>=5.0), red (<5.0)
          Warnings (smells): yellow
          Criticals: red
          Boxing: cyan
          Cyclic dependencies: red
          Level marker: yellow

        With --show-mi:
          Maintainability Index: green (>=80), yellow (>=60), red (<60)
        """;

    static string BuildUsageCacheSection(string tempDir) =>
        $$"""
        Cache:
          Formatted statusline output is cached in {{tempDir}}unilyze-sl-{hash}.txt
          Use --refresh to control cache lifetime (default: 60 seconds)
          A missing cache prints nothing (one empty status line render) and exits
          immediately while analysis runs in the background; the next render shows it
          A refresh lock ({{tempDir}}unilyze-sl-{hash}.lock) prevents concurrent refreshes
          Syntax-level parse cache (--incremental with --level syntax) is stored in
          <project>/.unilyze/cache/syntax/v1/ (auto-created; .gitignore contains *)
        """;

    static int PrintUsage()
    {
        Console.WriteLine(BuildUsageText());
        return 0;
    }
}
