using System.Diagnostics;

namespace Unilyze;

public sealed record FileChangeFrequency(string RelativePath, int ChangeCount);

public sealed record TypeHotspot(
    string TypeName,
    string Namespace,
    string Assembly,
    string? FilePath,
    int ChangeCount,
    double CodeHealth,
    double AverageCognitiveComplexity,
    int MaxCognitiveComplexity,
    double HotspotScore);

public sealed record HotspotResult(
    string ProjectPath,
    string Since,
    int TopN,
    IReadOnlyList<TypeHotspot> Hotspots);

public static class HotspotAnalyzer
{
    internal static IReadOnlyList<FileChangeFrequency> ParseGitLog(string gitLogOutput)
    {
        if (string.IsNullOrWhiteSpace(gitLogOutput))
            return [];

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in gitLogOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            counts[trimmed] = counts.GetValueOrDefault(trimmed) + 1;
        }

        return counts
            .Select(kv => new FileChangeFrequency(kv.Key, kv.Value))
            .OrderByDescending(f => f.ChangeCount)
            .ToList();
    }

    public static HotspotResult Analyze(
        IReadOnlyList<TypeMetrics> typeMetrics,
        IReadOnlyList<FileChangeFrequency> changeFrequencies,
        string projectPath,
        string since,
        int topN)
    {
        var normalizedProjectPath = Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar);

        // Build file path -> change count lookup (relative paths from git log)
        var changeByRelPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var freq in changeFrequencies)
            changeByRelPath[NormalizePath(freq.RelativePath)] = freq.ChangeCount;

        var hotspots = new List<TypeHotspot>();
        foreach (var tm in typeMetrics)
        {
            var changeCount = ResolveChangeCount(tm.FilePath, normalizedProjectPath, changeByRelPath);
            if (changeCount <= 0)
                continue;

            var score = changeCount * (10.0 - tm.CodeHealth);
            hotspots.Add(new TypeHotspot(
                tm.TypeName,
                tm.Namespace,
                tm.Assembly,
                tm.FilePath,
                changeCount,
                tm.CodeHealth,
                tm.AverageCognitiveComplexity,
                tm.MaxCognitiveComplexity,
                Math.Round(score, 1)));
        }

        var sorted = hotspots
            .OrderByDescending(h => h.HotspotScore)
            .Take(topN)
            .ToList();

        return new HotspotResult(projectPath, since, topN, sorted);
    }

    public static string RunGitLog(string repoPath, string since)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"-C \"{repoPath}\" log --format=format: --name-only --since={since} -- \"*.cs\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException("Failed to start git process");
        var stderrTask = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git log failed (exit {process.ExitCode}): {stderrTask.Result}");

        return output;
    }

    static int ResolveChangeCount(
        string? filePath,
        string normalizedProjectPath,
        Dictionary<string, int> changeByRelPath)
    {
        if (string.IsNullOrEmpty(filePath))
            return 0;

        var normalizedAbsolute = NormalizePath(Path.GetFullPath(filePath));

        // Try making it relative to project path
        var prefix = NormalizePath(normalizedProjectPath) + "/";
        if (normalizedAbsolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var relative = normalizedAbsolute[prefix.Length..];
            if (changeByRelPath.TryGetValue(relative, out var count))
                return count;
        }

        // Try matching by filename as fallback
        var fileName = Path.GetFileName(filePath);
        foreach (var kv in changeByRelPath)
        {
            if (Path.GetFileName(kv.Key).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        return 0;
    }

    static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimEnd('/');
}
