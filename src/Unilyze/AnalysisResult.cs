using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unilyze;

public sealed record AnalysisResult(
    string ProjectPath,
    DateTimeOffset AnalyzedAt,
    IReadOnlyList<AssemblyInfo> Assemblies,
    IReadOnlyList<TypeNodeInfo> Types,
    IReadOnlyList<TypeDependency> Dependencies,
    IReadOnlyList<TypeMetrics>? TypeMetrics = null,
    string? AnalysisLevel = null,
    IReadOnlyList<CyclicDependency>? CyclicDependencies = null);

public sealed record AssemblyInfo(
    string Name,
    string Directory,
    IReadOnlyList<string> References,
    AssemblyMetrics Metrics,
    AssemblyHealthMetrics? HealthMetrics = null);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [
        typeof(JsonStringEnumConverter<DependencyKind>),
        typeof(JsonStringEnumConverter<CodeSmellKind>),
        typeof(JsonStringEnumConverter<SmellSeverity>),
        typeof(JsonStringEnumConverter<ChangeStatus>),
        typeof(JsonStringEnumConverter<CycleLevel>)])]
[JsonSerializable(typeof(AnalysisResult))]
[JsonSerializable(typeof(DiffResult))]
[JsonSerializable(typeof(HotspotResult))]
[JsonSerializable(typeof(TrendResult))]
[JsonSerializable(typeof(MetricDelta<int>))]
[JsonSerializable(typeof(MetricDelta<double>))]
internal partial class AnalysisJsonContext : JsonSerializerContext;
