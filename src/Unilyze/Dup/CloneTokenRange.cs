namespace Unilyze.Dup;

internal readonly record struct CloneTokenRange(string FilePath, int StartIndex, int EndIndexExclusive);
