namespace Unilyze;

public sealed record QueryResult(
    string ProjectPath,
    DateTimeOffset AnalyzedAt,
    IReadOnlyList<TypeEvidencePack> Types);

public sealed record TypeEvidencePack(
    string TypeName,
    string? Namespace,
    string? QualifiedName,
    string? TypeId,
    string? Anchor,
    TypeEvidenceMetrics Metrics,
    IReadOnlyList<TypeEvidenceSmell> Smells,
    IReadOnlyList<TypeEvidenceDependencyGroup> InboundDependencies,
    IReadOnlyList<TypeEvidenceDependencyGroup> OutboundDependencies,
    IReadOnlyList<TypeEvidenceMethod> TopMethods,
    TypeEvidenceApiSurface? ApiSurface = null);

public sealed record TypeEvidenceApiSurface(
    bool HasDocComment,
    string? DocSummary,
    IReadOnlyList<string> PublicSignatures,
    IReadOnlyList<string> Identifiers,
    int DocumentedPublicMemberCount,
    int PublicMemberCount);

public sealed record TypeEvidenceMetrics(
    double CodeHealth,
    int? Cbo,
    double? Lcom,
    int? Dit,
    int? Wmc,
    int LineCount,
    int MethodCount,
    int MaxCognitiveComplexity,
    int? BoxingCount,
    int? ClosureCaptureCount,
    int? ParamsAllocationCount,
    string? CodeHealthCategory = null,
    double? CodeHealthV1 = null);

public sealed record TypeEvidenceSmell(
    CodeSmellKind Kind,
    SmellSeverity Severity,
    string? MethodName,
    string Message,
    string? Anchor,
    string? Id = null,
    string? Triage = null);

public sealed record TypeEvidenceDependencyGroup(
    DependencyKind Kind,
    int Count,
    IReadOnlyList<string> Peers);

public sealed record TypeEvidenceMethod(
    string MethodName,
    int CognitiveComplexity,
    string? Anchor);
