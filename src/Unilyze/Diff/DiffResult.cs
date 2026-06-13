using Unilyze.Detectors;
namespace Unilyze.Diff;

internal enum ChangeStatus { Improved, Degraded, Unchanged }

internal sealed record MetricDelta<T>(string Name, T Before, T After, T Delta) where T : struct;

internal sealed record MethodDiff(
    string MethodName,
    int ParameterCount,
    ChangeStatus Status,
    IReadOnlyList<MetricDelta<int>> IntDeltas);

internal sealed record SmellChange(CodeSmell Smell, bool IsResolved);

internal sealed record TypeDiff(
    string TypeKey,
    string TypeName,
    string Namespace,
    string Assembly,
    ChangeStatus Status,
    IReadOnlyList<MetricDelta<double>> DoubleDeltas,
    IReadOnlyList<MetricDelta<int>> IntDeltas,
    IReadOnlyList<MethodDiff> MethodDiffs,
    IReadOnlyList<SmellChange>? SmellChanges);

internal sealed record DiffSummary(
    int ImprovedCount,
    int DegradedCount,
    int UnchangedCount,
    int AddedCount,
    int RemovedCount);

internal sealed record DiffResult(
    string BeforePath,
    string AfterPath,
    DateTimeOffset BeforeAnalyzedAt,
    DateTimeOffset AfterAnalyzedAt,
    DiffSummary Summary,
    double DeltaScore,
    int LowRiskChangeCount,
    int HighRiskChangeCount,
    IReadOnlyList<TypeDiff> Improved,
    IReadOnlyList<TypeDiff> Degraded,
    IReadOnlyList<TypeDiff> Unchanged,
    IReadOnlyList<TypeDiff> Added,
    IReadOnlyList<TypeDiff> Removed);
