using System.Security.Cryptography;
using System.Text;

namespace Unilyze;

internal static class StatuslineRunner
{
    const string CachePrefix = "unilyze-sl-";
    const int DefaultRefreshSeconds = 60;

    public static int Run(string[] args)
    {
        if (ProgramHelpers.IsHelpRequest(args))
            return PrintUsage();

        var usageError = ProgramHelpers.ValidateStatuslineArgs(args);
        if (usageError != 0)
            return usageError;

        var opts = ProgramHelpers.ParseOptions(args);

        var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
        var refreshStr = opts.GetValueOrDefault("--refresh") ?? DefaultRefreshSeconds.ToString();
        if (!int.TryParse(refreshStr, out var refreshSeconds))
            refreshSeconds = DefaultRefreshSeconds;

        var levelStr = opts.GetValueOrDefault("--level");
        AnalysisLevel? requestedLevel = null;
        if (levelStr != null)
        {
            if (!AnalysisLevelOption.TryParse(levelStr, out var lvl))
            {
                Console.Error.WriteLine($"Unknown level: '{levelStr}'. Valid levels: syntax, core, full, complete");
                return 1;
            }
            requestedLevel = lvl;
        }

        var fullPath = ProgramHelpers.ResolveProjectRoot(path);
        var cacheHash = ComputePathHash(fullPath);
        var cacheDir = Path.GetTempPath();
        var cacheTxtPath = Path.Combine(cacheDir, $"{CachePrefix}{cacheHash}.txt");
        var lockPath = Path.Combine(cacheDir, $"{CachePrefix}{cacheHash}.lock");

        // Cache hit: output cached result
        if (File.Exists(cacheTxtPath))
        {
            var cacheAge = DateTimeOffset.UtcNow - new DateTimeOffset(File.GetLastWriteTimeUtc(cacheTxtPath));
            if (cacheAge.TotalSeconds < refreshSeconds)
            {
                Console.Write(File.ReadAllText(cacheTxtPath));
                return 0;
            }
        }

        // Try to acquire lock (non-blocking)
        FileStream? lockStream;
        try
        {
            lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        }
        catch (IOException)
        {
            // Another process is updating — output stale cache if available
            if (File.Exists(cacheTxtPath))
            {
                Console.Write(File.ReadAllText(cacheTxtPath));
                return 0;
            }
            return 1;
        }

        try
        {
            var config = UnilyzeConfig.LoadMerged(fullPath);
            var result = AnalysisPipeline.Build(fullPath, null, null, config.ExcludeDirs, requestedLevel);
            var summary = StatuslineFormatter.ComputeSummary(result);
            var formatted = StatuslineFormatter.Format(summary);

            File.WriteAllText(cacheTxtPath, formatted);
            Console.Write(formatted);
            return 0;
        }
        catch (Exception)
        {
            // Fallback to stale cache on error
            if (File.Exists(cacheTxtPath))
            {
                Console.Write(File.ReadAllText(cacheTxtPath));
                return 0;
            }
            return 1;
        }
        finally
        {
            lockStream.Dispose();
            try { File.Delete(lockPath); } catch { /* best-effort cleanup */ }
        }
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
            unilyze statusline - Output compact code health for status line display

            Usage:
              unilyze statusline                         Analyze current directory
              unilyze statusline -p <path>               Analyze specified project
              unilyze statusline -p <path> --refresh 30  Custom cache interval (seconds)

            Options:
              -p, --path     Project root (default: .)
              --refresh      Cache refresh interval in seconds (default: 60)
              --level        Pin analysis level: syntax, core, full, complete
              -h, --help     Show this help

            Output format: CH:9.4/3.2 MI:72 87smells 🔴5 📦12 ♻3 [core]
              CH:<avg>/<min> = Code Health average and minimum (1.0-10.0), always shown
              MI:<n>         = Average Maintainability Index (integer), always shown
              <n>smells      = Warning code smells count, always shown
              🔴<n>          = Critical code smells count (hidden if 0)
              📦<n>          = Boxing allocation count (hidden if 0)
              ♻<n>           = Cyclic dependency count (hidden if 0)
              [level]        = Analysis level marker, shown only below Complete
                               ([syntax] / [core] / [full])

            Color coding:
              Code Health (avg and min): green (>=8.0), yellow (>=5.0), red (<5.0)
              Maintainability Index: green (>=80), yellow (>=60), red (<60)
              Warnings (smells): yellow
              Criticals: red
              Boxing: cyan
              Cyclic dependencies: red
              Level marker: yellow

            Cache:
              Results are cached in {{tempDir}}unilyze-sl-{hash}.txt
              Use --refresh to control cache lifetime (default: 60 seconds)
            """;
    }

    static int PrintUsage()
    {
        Console.WriteLine(BuildUsageText());
        return 0;
    }
}
