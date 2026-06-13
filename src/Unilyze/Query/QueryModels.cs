using Unilyze.Detectors;
using Unilyze.Metrics;
using Unilyze.Pipeline;
namespace Unilyze.Query;

internal sealed record QueryResult(
    string ProjectPath,
    DateTimeOffset AnalyzedAt,
    IReadOnlyList<TypeEvidencePack> Types);

internal sealed record TypeEvidencePack(
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

internal sealed record TypeEvidenceApiSurface(
    bool HasDocComment,
    string? DocSummary,
    IReadOnlyList<string> PublicSignatures,
    IReadOnlyList<string> Identifiers,
    int DocumentedPublicMemberCount,
    int PublicMemberCount);

internal sealed record TypeEvidenceMetrics(
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

internal sealed record TypeEvidenceSmell(
    CodeSmellKind Kind,
    SmellSeverity Severity,
    string? MethodName,
    string Message,
    string? Anchor,
    string? Id = null,
    string? Triage = null);

internal sealed record TypeEvidenceDependencyGroup(
    DependencyKind Kind,
    int Count,
    IReadOnlyList<string> Peers);

internal sealed record TypeEvidenceMethod(
    string MethodName,
    int CognitiveComplexity,
    string? Anchor);
