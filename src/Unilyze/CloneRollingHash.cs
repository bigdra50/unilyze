namespace Unilyze;

internal static class CloneRollingHash
{
    const ulong HashBase = 257;
    const ulong HashMod = 1_000_000_007;

    public static Dictionary<ulong, List<(string File, int Start)>> IndexWindows(
        IReadOnlyList<(string FilePath, IReadOnlyList<NormalizedToken> Tokens)> files,
        int minTokens)
    {
        var buckets = new Dictionary<ulong, List<(string File, int Start)>>();
        foreach (var (filePath, tokens) in files)
        {
            if (tokens.Count < minTokens)
                continue;

            var (hash, pow) = ComputeInitialHash(tokens, minTokens);
            AddToBucket(buckets, hash, filePath, 0);

            for (var start = 1; start <= tokens.Count - minTokens; start++)
            {
                hash = RollHash(hash, tokens[start - 1].Text, tokens[start + minTokens - 1].Text, pow);
                AddToBucket(buckets, hash, filePath, start);
            }
        }

        return buckets;
    }

    static void AddToBucket(
        Dictionary<ulong, List<(string File, int Start)>> buckets,
        ulong hash,
        string filePath,
        int start)
    {
        if (!buckets.TryGetValue(hash, out var list))
        {
            list = [];
            buckets[hash] = list;
        }

        list.Add((filePath, start));
    }

    static (ulong Hash, ulong Pow) ComputeInitialHash(IReadOnlyList<NormalizedToken> tokens, int window)
    {
        ulong hash = 0;
        ulong pow = 1;
        for (var i = 0; i < window; i++)
        {
            hash = (hash * HashBase + TokenHash(tokens[i].Text)) % HashMod;
            if (i < window - 1)
                pow = pow * HashBase % HashMod;
        }

        return (hash, pow);
    }

    static ulong RollHash(ulong hash, string outgoing, string incoming, ulong pow)
    {
        var outValue = TokenHash(outgoing);
        hash = (hash + HashMod - (outValue * pow) % HashMod) % HashMod;
        hash = (hash * HashBase + TokenHash(incoming)) % HashMod;
        return hash;
    }

    static ulong TokenHash(string token)
    {
        ulong hash = 0;
        foreach (var ch in token)
            hash = (hash * HashBase + ch) % HashMod;
        return hash;
    }
}
