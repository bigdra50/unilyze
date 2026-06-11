namespace Unilyze;

internal readonly record struct NormalizedToken(string Text, int StartLine, int EndLine);

internal sealed record FileTokenSequence(string FilePath, IReadOnlyList<NormalizedToken> Tokens, int LineCount);

internal sealed record CloneOccurrence(string File, int StartLine, int EndLine);

internal sealed record CloneClass(int Id, int TokenCount, IReadOnlyList<CloneOccurrence> Occurrences)
{
    public string FindingKind => "DuplicatedCode";
}

internal sealed record CloneSummary(
    int AnalyzedFiles,
    int TotalLines,
    int TotalTokens,
    int DuplicatedLines,
    int DuplicatedTokens,
    double DuplicationPercent,
    int CloneClassCount,
    int SuppressedPairCount,
    int MinTokens);

internal sealed record CloneReport(
    string ProjectPath,
    string ToolVersion,
    int MetricsVersion,
    CloneSummary Summary,
    IReadOnlyList<CloneClass> CloneClasses);

internal sealed record DupAnalysisOptions(
    string Path,
    int MinTokens,
    IReadOnlyList<string> ThirdPartyDirs,
    bool IncludeThirdParty,
    IReadOnlyList<string>? ExcludeDirectories = null,
    bool ExcludeGeneratedCode = true,
    bool ApplyAnyDepthExcludes = true,
    int? MaxParallelism = null);
