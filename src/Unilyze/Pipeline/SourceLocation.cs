namespace Unilyze.Pipeline;

internal sealed record SourceLocation(
    int FileRef,
    int StartLine,
    int EndLine,
    string? SourceFile = null);
