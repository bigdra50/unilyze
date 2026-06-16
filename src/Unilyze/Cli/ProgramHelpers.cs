using Unilyze.Findings;
using Unilyze.Discovery;
using Unilyze.Output;
using Unilyze.Config;
using Unilyze.Pipeline;
using System.Text.Json;

namespace Unilyze.Cli;

internal static class ProgramHelpers
{
    public static Dictionary<string, string> ParseOptions(string[] args)
    {
        var opts = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith('-'))
                continue;

            if (IsBooleanOption(args[i]))
                opts[args[i]] = "true";
            else if (TryReadOptionValue(args, i, out var value))
                opts[args[i++]] = value;
        }
        return opts;
    }

    static bool IsBooleanOption(string option) =>
        option is "-h" or "--help" or "-v" or "--version" or "--no-open" or "--no-triage"
            or "--fail-on-regression" or "--fail-on-version-mismatch" or "--changed-only"
            or "--verbose" or "--quiet" or "--background-refresh" or "--no-bot-filter"
            or "--include-api-surface" or "--include-third-party"
            or "--incremental" or "--resolve-nuget" or "--include-generated"
            or "--codehealth-v1" or "--show-mi";

    static bool TryReadOptionValue(string[] args, int optionIndex, out string value)
    {
        if (optionIndex + 1 < args.Length)
        {
            value = args[optionIndex + 1];
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// True when <paramref name="flag"/> appears in <paramref name="args"/> but
    /// has no value token after it (last position, or immediately followed by
    /// another option). ParseOptions silently drops such flags, which would turn
    /// a value-taking gate flag into a no-op (false green in CI). Callers use
    /// this to surface a usage error instead.
    /// </summary>
    public static bool HasFlagWithoutValue(string[] args, string flag)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] != flag)
                continue;
            if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                return true;
        }
        return false;
    }

    public static int? TryApplyBaseline(
        AnalysisResult result,
        string projectRoot,
        string? baselinePath,
        out AnalysisResult updated)
    {
        updated = result;
        if (baselinePath is null)
            return null;

        var resolvedPath = BaselineFile.ResolvePath(projectRoot, baselinePath);
        if (!File.Exists(resolvedPath))
        {
            Console.Error.WriteLine($"Baseline file not found: {resolvedPath}");
            return 1;
        }

        try
        {
            var baseline = BaselineFile.Load(resolvedPath);
            BaselineMatcher.WarnIfMetricsVersionMismatch(baseline);
            updated = BaselineMatcher.Apply(result, baseline, out var stats);
            BaselineMatcher.WriteSummary(stats);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    public static string? ResolveBaselineOption(
        IReadOnlyDictionary<string, string> opts,
        UnilyzeConfig config)
        => opts.GetValueOrDefault("--baseline") ?? config.Baseline;

    public static OutputFormat ResolveFormat(string? formatStr, string? output)
    {
        if (formatStr != null)
        {
            return formatStr.ToLowerInvariant() switch
            {
                "json" => OutputFormat.Json,
                "html" => OutputFormat.Html,
                "sarif" => OutputFormat.Sarif,
                "markdown" => OutputFormat.Markdown,
                _ => throw new ArgumentException($"Unknown format: '{formatStr}'. Valid formats: json, html, sarif, markdown")
            };
        }

        if (output != null)
        {
            return Path.GetExtension(output).ToLowerInvariant() switch
            {
                ".html" or ".htm" => OutputFormat.Html,
                ".json" => OutputFormat.Json,
                ".sarif" => OutputFormat.Sarif,
                _ => OutputFormat.Json
            };
        }

        return OutputFormat.Html;
    }

    public static IReadOnlyList<AsmdefInfo> FilterAssemblies(
        IReadOnlyList<AsmdefInfo> asmdefs, string? prefix, string? assemblyFilter)
    {
        if (assemblyFilter != null)
        {
            var filtered = asmdefs.Where(a =>
                a.Name.Equals(assemblyFilter, StringComparison.OrdinalIgnoreCase)
                || a.Name.EndsWith("." + assemblyFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (filtered.Count == 0)
                throw new InvalidOperationException($"Assembly '{assemblyFilter}' not found.");
            return filtered;
        }

        if (prefix != null)
            return asmdefs.Where(a => a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

        return asmdefs.ToList();
    }

    public static string? DetectCommonPrefix(IReadOnlyList<AsmdefInfo> asmdefs)
    {
        var names = asmdefs.Select(a => a.Name).ToList();
        if (names.Count < 2) return null;
        var parts = names[0].Split('.');
        for (var len = parts.Length; len > 0; len--)
        {
            var candidate = string.Join(".", parts.Take(len)) + ".";
            if (names.All(n => n.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
        return null;
    }

    public static IReadOnlyList<string> ParseMultiValueOption(string[] args, string key)
    {
        var values = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == key)
            {
                values.Add(args[i + 1]);
                i++;
            }
        }
        return values;
    }

    public static string ResolveAssetsDir(string path)
    {
        if (Directory.Exists(Path.Combine(path, "Assets")))
            return Path.Combine(path, "Assets");
        if (Path.GetFileName(path) == "Assets")
            return path;
        return path;
    }

    public static string ResolveProjectRoot(string path)
    {
        var dir = Path.GetFullPath(path);
        for (var i = 0; i < 5; i++)
        {
            if (File.Exists(Path.Combine(dir, "ProjectSettings", "ProjectVersion.txt")))
                return dir;
            if (Directory.EnumerateFiles(dir, "*.sln", SearchOption.TopDirectoryOnly).Any())
                return dir;
            if (Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).Any())
                return dir;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent is null || parent == dir) break;
            dir = parent;
        }

        return Path.GetFullPath(path);
    }

    public static string ResolveProjectKind(string projectRoot)
    {
        if (File.Exists(Path.Combine(projectRoot, "ProjectSettings", "ProjectVersion.txt")))
            return "unity";

        if (CsprojParser.DiscoverCsprojFiles(projectRoot).Count > 0)
            return "dotnet";

        if (Directory.EnumerateFiles(projectRoot, "*.sln", SearchOption.AllDirectories).Any())
            return "dotnet";

        return "unknown";
    }

    public static int WriteOutput(string content, string? outputPath)
    {
        if (outputPath != null)
        {
            File.WriteAllText(outputPath, content);
            Console.Error.WriteLine($"Written to {outputPath}");
            return 0;
        }
        Console.Write(content);
        return 0;
    }

    public static void TryOpenInBrowser(string pathOrUrl)
    {
        try
        {
            var url = ResolveOpenTarget(pathOrUrl);
            if (OperatingSystem.IsMacOS())
                System.Diagnostics.Process.Start("open", url)?.Dispose();
            else if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            else if (OperatingSystem.IsLinux())
                System.Diagnostics.Process.Start("xdg-open", url)?.Dispose();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to open browser automatically: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves a path-or-URL into a target the OS browser launcher accepts. An absolute
    /// http/https URL (e.g. the <c>serve</c> loopback address) passes through unchanged;
    /// anything else is treated as a local filesystem path and turned into a file:// URL.
    /// Without this split, serve's <c>http://127.0.0.1:PORT/</c> would be mangled by
    /// <see cref="Path.GetFullPath(string)"/> into a bogus <c>file://.../http:/...</c> path.
    /// </summary>
    public static string ResolveOpenTarget(string pathOrUrl)
    {
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return pathOrUrl;
        return "file://" + Path.GetFullPath(pathOrUrl);
    }
    public static ReferenceAnalysisSettings LoadReferenceAnalysisSettings(
        string projectRoot,
        IReadOnlyDictionary<string, string>? opts = null)
    {
        opts ??= new Dictionary<string, string>();
        return ReferenceAnalysisSettings.LoadMerged(
            projectRoot,
            opts.ContainsKey("--resolve-nuget"),
            opts.ContainsKey("--include-generated"),
            opts.GetValueOrDefault("--tfm"));
    }


}
