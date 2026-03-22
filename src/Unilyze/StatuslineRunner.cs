using System.Security.Cryptography;
using System.Text;

namespace Unilyze;

internal static class StatuslineRunner
{
    const string CachePrefix = "unilyze-sl-";
    const int DefaultRefreshSeconds = 60;

    public static int Run(string[] args)
    {
        var opts = ProgramHelpers.ParseOptions(args);
        if (opts.ContainsKey("-h") || opts.ContainsKey("--help"))
            return PrintUsage();

        var path = opts.GetValueOrDefault("-p") ?? opts.GetValueOrDefault("--path") ?? ".";
        var refreshStr = opts.GetValueOrDefault("--refresh") ?? DefaultRefreshSeconds.ToString();
        if (!int.TryParse(refreshStr, out var refreshSeconds))
            refreshSeconds = DefaultRefreshSeconds;

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
            var result = AnalysisPipeline.Build(fullPath, null, null);
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

    static int PrintUsage()
    {
        Console.WriteLine("""
            unilyze statusline - Output compact code health for status line display

            Usage:
              unilyze statusline                         Analyze current directory
              unilyze statusline -p <path>               Analyze specified project
              unilyze statusline -p <path> --refresh 30  Custom cache interval (seconds)

            Options:
              -p, --path     Project root (default: .)
              --refresh      Cache refresh interval in seconds (default: 60)
              -h, --help     Show this help

            Output format: CH:9.4 ⚠87 🔴5
              CH  = Average Code Health (1.0-10.0)
              ⚠   = Warning code smells count
              🔴  = Critical code smells count (hidden if 0)

            Color coding:
              Code Health: green (>=8.0), yellow (>=5.0), red (<5.0)
              Warnings: yellow
              Criticals: red

            Cache:
              Results are cached in /tmp/unilyze-sl-{hash}.txt
              Use --refresh to control cache lifetime (default: 60 seconds)
            """);
        return 0;
    }
}
