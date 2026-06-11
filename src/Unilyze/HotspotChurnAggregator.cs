namespace Unilyze;

internal static class HotspotChurnAggregator
{
    internal static (IReadOnlyList<GitCommitRecord> Included, int Excluded) ApplyBotFilter(
        IReadOnlyList<GitCommitRecord> commits,
        BotAuthorMatcher matcher,
        bool botFilterEnabled)
    {
        if (!botFilterEnabled)
            return (commits, 0);

        var included = new List<GitCommitRecord>();
        var excluded = 0;
        foreach (var commit in commits)
        {
            if (matcher.IsBot(commit.AuthorName, commit.AuthorEmail))
            {
                excluded++;
                continue;
            }

            included.Add(commit);
        }

        return (included, excluded);
    }

    internal static double ComputeDecayWeight(
        long commitTimestamp,
        long anchorTimestamp,
        TimeSpan halfLife)
    {
        if (halfLife <= TimeSpan.Zero)
            return 1.0;

        var ageSeconds = Math.Max(0, anchorTimestamp - commitTimestamp);
        return Math.Pow(2.0, -ageSeconds / halfLife.TotalSeconds);
    }

    internal static IReadOnlyList<FileChangeFrequency> AggregateFileChanges(
        IReadOnlyList<GitCommitRecord> commits,
        TimeSpan? halfLife)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var weighted = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var useDecay = halfLife.HasValue;
        var anchor = commits.Count > 0 ? commits.Max(c => c.TimestampUnix) : 0L;

        foreach (var commit in commits)
        {
            var weight = useDecay
                ? ComputeDecayWeight(commit.TimestampUnix, anchor, halfLife!.Value)
                : 1.0;

            foreach (var file in commit.ChangedFiles)
            {
                var path = file.Trim();
                if (path.Length == 0)
                    continue;

                counts[path] = counts.GetValueOrDefault(path) + 1;
                weighted[path] = weighted.GetValueOrDefault(path) + weight;
            }
        }

        return counts
            .Select(kv => new FileChangeFrequency(
                kv.Key,
                kv.Value,
                useDecay ? Math.Round(weighted[kv.Key], 4) : 0))
            .OrderByDescending(f => useDecay ? f.WeightedChurn : f.ChangeCount)
            .ToList();
    }

    internal static (int ChangeCount, double? WeightedChurn, double ChurnForScore) SummarizeCommits(
        IReadOnlyList<GitCommitRecord> commits,
        TimeSpan? halfLife)
    {
        var changeCount = commits.Count;
        if (changeCount == 0)
            return (0, null, 0);

        if (!halfLife.HasValue)
            return (changeCount, null, changeCount);

        var anchor = commits.Max(c => c.TimestampUnix);
        var weighted = Math.Round(
            commits.Sum(c => ComputeDecayWeight(c.TimestampUnix, anchor, halfLife.Value)),
            4);
        return (changeCount, weighted, weighted);
    }
}
