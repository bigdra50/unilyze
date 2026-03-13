namespace Unilyze;

public enum ChangeStatus { Improved, Degraded, Unchanged }

public sealed record MetricDelta<T>(string Name, T Before, T After, T Delta) where T : struct;

public sealed record MethodDiff(
    string MethodName,
    int ParameterCount,
    ChangeStatus Status,
    IReadOnlyList<MetricDelta<int>> IntDeltas);

public sealed record SmellChange(CodeSmell Smell, bool IsResolved);

public sealed record TypeDiff(
    string TypeKey,
    string TypeName,
    string Namespace,
    string Assembly,
    ChangeStatus Status,
    IReadOnlyList<MetricDelta<double>> DoubleDeltas,
    IReadOnlyList<MetricDelta<int>> IntDeltas,
    IReadOnlyList<MethodDiff> MethodDiffs,
    IReadOnlyList<SmellChange>? SmellChanges);

public sealed record DiffSummary(
    int ImprovedCount,
    int DegradedCount,
    int UnchangedCount,
    int AddedCount,
    int RemovedCount);

public sealed record DiffResult(
    string BeforePath,
    string AfterPath,
    DateTimeOffset BeforeAnalyzedAt,
    DateTimeOffset AfterAnalyzedAt,
    DiffSummary Summary,
    IReadOnlyList<TypeDiff> Improved,
    IReadOnlyList<TypeDiff> Degraded,
    IReadOnlyList<TypeDiff> Unchanged,
    IReadOnlyList<TypeDiff> Added,
    IReadOnlyList<TypeDiff> Removed);
