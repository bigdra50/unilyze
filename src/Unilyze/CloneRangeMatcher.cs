namespace Unilyze;

internal static class CloneRangeMatcher
{
    public static List<CloneTokenRange> CollectMaximalRanges(
        IReadOnlyList<(string FilePath, IReadOnlyList<NormalizedToken> Tokens)> files,
        Dictionary<ulong, List<(string File, int Start)>> buckets,
        int minTokens)
    {
        var fileLookup = files.ToDictionary(f => f.FilePath, f => f.Tokens, StringComparer.Ordinal);
        var ranges = new List<CloneTokenRange>();
        var seen = new HashSet<(string File, int Start, int End)>();

        foreach (var bucket in buckets.Values)
            CollectFromBucket(bucket, fileLookup, minTokens, ranges, seen);

        return MergeOverlaps(ranges);
    }

    static void CollectFromBucket(
        List<(string File, int Start)> bucket,
        IReadOnlyDictionary<string, IReadOnlyList<NormalizedToken>> fileLookup,
        int minTokens,
        List<CloneTokenRange> ranges,
        HashSet<(string File, int Start, int End)> seen)
    {
        if (bucket.Count < 2)
            return;

        for (var i = 0; i < bucket.Count; i++)
        {
            for (var j = i + 1; j < bucket.Count; j++)
                TryCollectPair(bucket[i], bucket[j], fileLookup, minTokens, ranges, seen);
        }
    }

    static void TryCollectPair(
        (string File, int Start) left,
        (string File, int Start) right,
        IReadOnlyDictionary<string, IReadOnlyList<NormalizedToken>> fileLookup,
        int minTokens,
        List<CloneTokenRange> ranges,
        HashSet<(string File, int Start, int End)> seen)
    {
        if (left.File == right.File && WindowsOverlap(left.Start, right.Start, minTokens))
            return;

        if (!SequencesMatch(fileLookup, left, right, minTokens))
            return;

        var extended = ExtendMatch(fileLookup, left, right, minTokens);
        if (extended.LeftEnd - extended.LeftStart < minTokens)
            return;

        var leftRange = new CloneTokenRange(left.File, extended.LeftStart, extended.LeftEnd);
        var rightRange = new CloneTokenRange(right.File, extended.RightStart, extended.RightEnd);

        if (leftRange.FilePath == rightRange.FilePath && RangesOverlap(leftRange, rightRange))
            return;

        TryAddRange(ranges, seen, leftRange);
        TryAddRange(ranges, seen, rightRange);
    }

    static void TryAddRange(
        List<CloneTokenRange> ranges,
        HashSet<(string File, int Start, int End)> seen,
        CloneTokenRange range)
    {
        var key = (range.FilePath, range.StartIndex, range.EndIndexExclusive);
        if (!seen.Add(key))
            return;
        ranges.Add(range);
    }

    static bool SequencesMatch(
        IReadOnlyDictionary<string, IReadOnlyList<NormalizedToken>> fileLookup,
        (string File, int Start) left,
        (string File, int Start) right,
        int length)
    {
        var leftTokens = fileLookup[left.File];
        var rightTokens = fileLookup[right.File];
        for (var offset = 0; offset < length; offset++)
        {
            if (leftTokens[left.Start + offset].Text != rightTokens[right.Start + offset].Text)
                return false;
        }

        return true;
    }

    static (int LeftStart, int LeftEnd, int RightStart, int RightEnd) ExtendMatch(
        IReadOnlyDictionary<string, IReadOnlyList<NormalizedToken>> fileLookup,
        (string File, int Start) left,
        (string File, int Start) right,
        int minTokens)
    {
        var leftTokens = fileLookup[left.File];
        var rightTokens = fileLookup[right.File];
        var leftStart = left.Start;
        var rightStart = right.Start;
        var leftEnd = left.Start + minTokens;
        var rightEnd = right.Start + minTokens;

        while (leftStart > 0 && rightStart > 0
               && leftTokens[leftStart - 1].Text == rightTokens[rightStart - 1].Text)
        {
            leftStart--;
            rightStart--;
        }

        while (leftEnd < leftTokens.Count && rightEnd < rightTokens.Count
               && leftTokens[leftEnd].Text == rightTokens[rightEnd].Text)
        {
            leftEnd++;
            rightEnd++;
        }

        return (leftStart, leftEnd, rightStart, rightEnd);
    }

    static bool WindowsOverlap(int leftStart, int rightStart, int length) =>
        leftStart < rightStart + length && rightStart < leftStart + length;

    static List<CloneTokenRange> MergeOverlaps(IReadOnlyList<CloneTokenRange> ranges)
    {
        var merged = new List<CloneTokenRange>();
        foreach (var group in ranges.GroupBy(r => r.FilePath, StringComparer.Ordinal))
        {
            var sorted = group.OrderBy(r => r.StartIndex).ToList();
            if (sorted.Count == 0)
                continue;

            var current = sorted[0];
            for (var i = 1; i < sorted.Count; i++)
            {
                var next = sorted[i];
                if (RangesOverlap(current, next))
                {
                    current = new CloneTokenRange(
                        current.FilePath,
                        Math.Min(current.StartIndex, next.StartIndex),
                        Math.Max(current.EndIndexExclusive, next.EndIndexExclusive));
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }

            merged.Add(current);
        }

        return merged;
    }

    static bool RangesOverlap(CloneTokenRange left, CloneTokenRange right) =>
        left.FilePath == right.FilePath
        && left.StartIndex < right.EndIndexExclusive
        && right.StartIndex < left.EndIndexExclusive;
}
