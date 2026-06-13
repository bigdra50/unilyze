namespace Unilyze.Dup;

internal static class CloneClassGrouper
{
    public static IReadOnlyList<CloneClass> GroupIntoClasses(
        IReadOnlyList<CloneTokenRange> ranges,
        IReadOnlyList<(string FilePath, IReadOnlyList<NormalizedToken> Tokens)> files)
    {
        var fileLookup = files.ToDictionary(f => f.FilePath, f => f.Tokens, StringComparer.Ordinal);
        var bySequence = new Dictionary<string, List<CloneTokenRange>>(StringComparer.Ordinal);

        foreach (var range in ranges)
        {
            var key = SequenceKey(fileLookup[range.FilePath], range.StartIndex, range.EndIndexExclusive);
            if (!bySequence.TryGetValue(key, out var group))
            {
                group = [];
                bySequence[key] = group;
            }

            group.Add(range);
        }

        var classes = new List<CloneClass>();
        var id = 1;
        foreach (var group in bySequence.Values)
        {
            if (!ShouldReportGroup(group))
                continue;

            var distinctRanges = group
                .GroupBy(r => (r.FilePath, r.StartIndex, r.EndIndexExclusive))
                .Select(g => g.First())
                .ToList();

            if (distinctRanges.Count < 2)
                continue;

            var tokenCount = distinctRanges[0].EndIndexExclusive - distinctRanges[0].StartIndex;
            var occurrences = BuildOccurrences(distinctRanges, fileLookup);
            classes.Add(new CloneClass(id++, tokenCount, occurrences));
        }

        return classes.OrderByDescending(c => c.TokenCount).ThenBy(c => c.Id).ToList();
    }

    static bool ShouldReportGroup(IReadOnlyList<CloneTokenRange> group)
    {
        if (group.Count >= 2)
            return true;

        return group.Select(r => r.FilePath).Distinct(StringComparer.Ordinal).Count() >= 2;
    }

    static List<CloneOccurrence> BuildOccurrences(
        IReadOnlyList<CloneTokenRange> ranges,
        IReadOnlyDictionary<string, IReadOnlyList<NormalizedToken>> fileLookup)
    {
        var occurrences = new List<CloneOccurrence>(ranges.Count);
        foreach (var range in ranges)
        {
            var tokens = fileLookup[range.FilePath];
            occurrences.Add(new CloneOccurrence(
                range.FilePath,
                tokens[range.StartIndex].StartLine,
                tokens[range.EndIndexExclusive - 1].EndLine));
        }

        return occurrences
            .OrderBy(o => o.File, StringComparer.Ordinal)
            .ThenBy(o => o.StartLine)
            .ToList();
    }

    static string SequenceKey(IReadOnlyList<NormalizedToken> tokens, int start, int endExclusive)
    {
        return string.Join('\u001f', tokens.Skip(start).Take(endExclusive - start).Select(t => t.Text));
    }
}
