namespace Unilyze;

internal static class HotspotMethodAnalyzer
{
    internal static IReadOnlyList<MethodHotspot> Analyze(
        string repoPath,
        string targetFile,
        IReadOnlyList<TypeMetrics> typeMetrics,
        string since,
        BotAuthorMatcher matcher,
        bool botFilterEnabled,
        TimeSpan? halfLife)
    {
        var normalizedTarget = HotspotPathHelper.NormalizePath(targetFile);
        var matchingTypes = typeMetrics
            .Where(t => t.FilePath is not null
                        && HotspotPathHelper.NormalizePath(
                            HotspotPathHelper.GetRelativePath(repoPath, t.FilePath)) == normalizedTarget)
            .ToList();

        var methodHotspots = new List<MethodHotspot>();
        foreach (var type in matchingTypes)
        {
            foreach (var method in type.Methods)
            {
                var hotspot = TryAnalyzeMethod(
                    repoPath, since, type, method, matcher, botFilterEnabled, halfLife);
                if (hotspot is not null)
                    methodHotspots.Add(hotspot);
            }
        }

        return methodHotspots
            .OrderByDescending(m => m.HotspotScore)
            .ToList();
    }

    static MethodHotspot? TryAnalyzeMethod(
        string repoPath,
        string since,
        TypeMetrics type,
        MethodMetrics method,
        BotAuthorMatcher matcher,
        bool botFilterEnabled,
        TimeSpan? halfLife)
    {
        if (method.StartLine is null or <= 0 || method.LineCount <= 0)
            return null;

        var startLine = method.StartLine.Value;
        var endLine = startLine + method.LineCount - 1;
        var relPath = HotspotPathHelper.GetRelativePath(repoPath, type.FilePath!);

        IReadOnlyList<GitCommitRecord> commits;
        try
        {
            var output = RunMethodGitLog(repoPath, since, relPath, startLine, endLine);
            commits = HotspotCommitParser.ParseCommitLog(output);
        }
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine(
                $"Warning: skipping method {type.TypeName}.{method.MethodName} (git log -L failed for lines {startLine}-{endLine})");
            return null;
        }

        var (included, _) = HotspotChurnAggregator.ApplyBotFilter(commits, matcher, botFilterEnabled);
        var (changeCount, weightedChurn, churnForScore) =
            HotspotChurnAggregator.SummarizeCommits(included, halfLife);
        if (changeCount <= 0)
            return null;

        var score = churnForScore * method.CognitiveComplexity;
        return new MethodHotspot(
            method.MethodName,
            type.TypeName,
            type.Namespace,
            startLine,
            endLine,
            changeCount,
            weightedChurn,
            method.CognitiveComplexity,
            Math.Round(score, 1));
    }

    static string RunMethodGitLog(
        string repoPath,
        string since,
        string relativeFilePath,
        int startLine,
        int endLine)
    {
        return GitProcess.Run(
            repoPath,
            "log",
            "--since=" + since,
            "-L",
            startLine.ToString() + "," + endLine.ToString() + ":" + relativeFilePath,
            "-s",
            "--format=%x01%H%x1f%an%x1f%ae%x1f%at");
    }
}
