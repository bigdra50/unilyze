namespace Unilyze.Pipeline;

internal sealed record TypeApiSurface(
    string TypeId,
    string QualifiedName,
    bool HasDocComment,
    string? DocSummary,
    IReadOnlyList<string> PublicSignatures,
    IReadOnlyList<string> Identifiers,
    int DocumentedPublicMemberCount,
    int PublicMemberCount);
