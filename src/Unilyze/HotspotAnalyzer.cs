namespace Unilyze;

public sealed record FileChangeFrequency(string RelativePath, int ChangeCount, double WeightedChurn = 0);

public sealed record GitCommitRecord(
    string Hash,
    string AuthorName,
    string AuthorEmail,
    long TimestampUnix,
    IReadOnlyList<string> ChangedFiles);

public sealed record TypeHotspot(
    string TypeName,
    string Namespace,
    string Assembly,
    string? FilePath,
    int ChangeCount,
    double? WeightedChurn,
    double CodeHealth,
    double AverageCognitiveComplexity,
    int MaxCognitiveComplexity,
    double HotspotScore);

public sealed record MethodHotspot(
    string MethodName,
    string TypeName,
    string? Namespace,
    int StartLine,
    int EndLine,
    int ChangeCount,
    double? WeightedChurn,
    int CognitiveComplexity,
    double HotspotScore);

public sealed record HotspotResult(
    string ProjectPath,
    string Since,
    int TopN,
    IReadOnlyList<TypeHotspot> Hotspots,
    bool BotFilter = true,
    int BotCommitsExcluded = 0,
    string? HalfLife = null,
    IReadOnlyList<MethodHotspot>? MethodHotspots = null);

public static class HotspotAnalyzer
{
    internal static IReadOnlyList<GitCommitRecord> ParseCommitLog(string gitLogOutput) =>
        HotspotCommitParser.ParseCommitLog(gitLogOutput);

    internal static IEnumerable<string> SplitLines(string text) =>
        HotspotCommitParser.SplitLines(text);

    internal static (IReadOnlyList<GitCommitRecord> Included, int Excluded) ApplyBotFilter(
        IReadOnlyList<GitCommitRecord> commits,
        BotAuthorMatcher matcher,
        bool botFilterEnabled) =>
        HotspotChurnAggregator.ApplyBotFilter(commits, matcher, botFilterEnabled);

    internal static double ComputeDecayWeight(
        long commitTimestamp,
        long anchorTimestamp,
        TimeSpan halfLife) =>
        HotspotChurnAggregator.ComputeDecayWeight(commitTimestamp, anchorTimestamp, halfLife);

    internal static IReadOnlyList<FileChangeFrequency> AggregateFileChanges(
        IReadOnlyList<GitCommitRecord> commits,
        TimeSpan? halfLife) =>
        HotspotChurnAggregator.AggregateFileChanges(commits, halfLife);

    internal static IReadOnlyList<FileChangeFrequency> ParseGitLog(string gitLogOutput) =>
        HotspotCommitParser.ParseLegacyNameOnlyLog(gitLogOutput);

    internal static HotspotResult Analyze(
        IReadOnlyList<TypeMetrics> typeMetrics,
        IReadOnlyList<FileChangeFrequency> changeFrequencies,
        HotspotAnalysisContext context)
    {
        var normalizedProjectPath = Path.GetFullPath(context.ProjectPath)
            .TrimEnd(Path.DirectorySeparatorChar);

        var changeByRelPath = new Dictionary<string, FileChangeFrequency>(StringComparer.OrdinalIgnoreCase);
        foreach (var freq in changeFrequencies)
            changeByRelPath[HotspotPathHelper.NormalizePath(freq.RelativePath)] = freq;

        var useDecay = context.HalfLifeSpan.HasValue;
        var hotspots = new List<TypeHotspot>();
        foreach (var tm in typeMetrics)
        {
            var freq = HotspotPathHelper.ResolveFileFrequency(
                tm.FilePath, normalizedProjectPath, changeByRelPath);
            if (freq is null || freq.ChangeCount <= 0)
                continue;

            var churn = useDecay ? freq.WeightedChurn : freq.ChangeCount;
            hotspots.Add(new TypeHotspot(
                tm.TypeName,
                tm.Namespace,
                tm.Assembly,
                tm.FilePath,
                freq.ChangeCount,
                useDecay ? Math.Round(freq.WeightedChurn, 4) : null,
                tm.CodeHealth,
                tm.AverageCognitiveComplexity,
                tm.MaxCognitiveComplexity,
                Math.Round(churn * (10.0 - tm.CodeHealth), 1)));
        }

        var sorted = hotspots
            .OrderByDescending(h => h.HotspotScore)
            .Take(context.TopN)
            .ToList();

        return new HotspotResult(
            context.ProjectPath,
            context.Since,
            context.TopN,
            sorted,
            context.BotFilter,
            context.BotCommitsExcluded,
            context.HalfLife);
    }

    internal static IReadOnlyList<MethodHotspot> AnalyzeMethods(
        string repoPath,
        string targetFile,
        IReadOnlyList<TypeMetrics> typeMetrics,
        string since,
        BotAuthorMatcher matcher,
        bool botFilterEnabled,
        TimeSpan? halfLife) =>
        HotspotMethodAnalyzer.Analyze(
            repoPath, targetFile, typeMetrics, since, matcher, botFilterEnabled, halfLife);

    public static string RunGitLog(string repoPath, string since) =>
        GitProcess.Run(
            repoPath,
            "log",
            "--since=" + since,
            "--format=%x01%H%x1f%an%x1f%ae%x1f%at",
            "--name-only",
            "--",
            "*.cs");
}
