using System.Text.Json.Serialization;

namespace Unilyze.Pipeline;

internal sealed record SourceLocation(
    int FileRef,
    int StartLine,
    int EndLine,
    [property: JsonIgnore] string? SourceFile = null);
