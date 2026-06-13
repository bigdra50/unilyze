namespace Unilyze.Dup;

internal static class ThirdPartyCloneFilter
{
    public static readonly string[] DefaultRelativeDirs =
    [
        "Assets/Plugins",
        "Assets/Standard Assets",
        "Assets/AssetStoreTools",
    ];

    public static IReadOnlyList<string> ResolveRoots(string projectRoot, IReadOnlyList<string> configuredDirs)
    {
        var roots = new List<string>();
        foreach (var dir in configuredDirs)
        {
            var full = Path.IsPathRooted(dir)
                ? Path.GetFullPath(dir)
                : Path.GetFullPath(Path.Combine(projectRoot, dir));
            roots.Add(full);
        }

        return roots;
    }

    public static (IReadOnlyList<CloneClass> Classes, int SuppressedPairCount) Apply(
        IReadOnlyList<CloneClass> classes,
        IReadOnlyList<string> thirdPartyRoots,
        bool includeThirdParty)
    {
        if (includeThirdParty || thirdPartyRoots.Count == 0)
            return (classes, 0);

        var filtered = new List<CloneClass>();
        var suppressedPairs = 0;

        foreach (var cloneClass in classes)
        {
            var (keep, suppressed) = FilterClass(cloneClass, thirdPartyRoots);
            suppressedPairs += suppressed;
            if (keep)
                filtered.Add(cloneClass);
        }

        return (filtered, suppressedPairs);
    }

    static (bool Keep, int SuppressedPairs) FilterClass(CloneClass cloneClass, IReadOnlyList<string> thirdPartyRoots)
    {
        var occurrences = cloneClass.Occurrences;
        if (occurrences.Count < 2)
            return (false, 0);

        var hasActionablePair = false;
        var suppressedPairs = 0;

        for (var i = 0; i < occurrences.Count; i++)
        {
            for (var j = i + 1; j < occurrences.Count; j++)
            {
                if (BothInSameThirdParty(occurrences[i].File, occurrences[j].File, thirdPartyRoots))
                    suppressedPairs++;
                else
                    hasActionablePair = true;
            }
        }

        return (hasActionablePair, suppressedPairs);
    }

    static bool BothInSameThirdParty(string leftFile, string rightFile, IReadOnlyList<string> thirdPartyRoots)
    {
        foreach (var root in thirdPartyRoots)
        {
            if (IsUnderRoot(leftFile, root) && IsUnderRoot(rightFile, root))
                return true;
        }

        return false;
    }

    static bool IsUnderRoot(string filePath, string root)
    {
        var normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return Path.GetFullPath(filePath).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
