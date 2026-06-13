namespace Unilyze.Dup;

internal static class CloneReportBuilder
{
    public static CloneSummary BuildSummary(
        IReadOnlyList<FileTokenSequence> files,
        IReadOnlyList<CloneClass> cloneClasses,
        int suppressedPairCount,
        int minTokens)
    {
        var totalLines = files.Sum(f => f.LineCount);
        var totalTokens = files.Sum(f => f.Tokens.Count);
        var duplicatedLineKeys = new HashSet<(string File, int Line)>();
        var duplicatedTokens = 0;

        foreach (var cloneClass in cloneClasses)
        {
            duplicatedTokens += cloneClass.TokenCount * cloneClass.Occurrences.Count;
            foreach (var occurrence in cloneClass.Occurrences)
            {
                for (var line = occurrence.StartLine; line <= occurrence.EndLine; line++)
                    duplicatedLineKeys.Add((occurrence.File, line));
            }
        }

        var duplicatedLines = duplicatedLineKeys.Count;
        var duplicationPercent = totalLines > 0
            ? duplicatedLines * 100.0 / totalLines
            : 0.0;

        return new CloneSummary(
            files.Count,
            totalLines,
            totalTokens,
            duplicatedLines,
            duplicatedTokens,
            duplicationPercent,
            cloneClasses.Count,
            suppressedPairCount,
            minTokens);
    }
}
