namespace Unilyze;

internal static class CloneDetector
{
    public static IReadOnlyList<CloneClass> Detect(IReadOnlyList<FileTokenSequence> files, int minTokens)
    {
        var indexedFiles = files
            .Where(f => f.Tokens.Count >= minTokens)
            .Select(f => (f.FilePath, Tokens: f.Tokens))
            .ToList();

        if (indexedFiles.Count == 0)
            return [];

        var windows = CloneRollingHash.IndexWindows(indexedFiles, minTokens);
        var maximal = CloneRangeMatcher.CollectMaximalRanges(indexedFiles, windows, minTokens);
        return CloneClassGrouper.GroupIntoClasses(maximal, indexedFiles);
    }
}
