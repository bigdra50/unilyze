using Unilyze.Query;
using Unilyze.Diff;
using Unilyze.History;
using Unilyze.Discovery;
using Unilyze.Detectors;
using Unilyze.Metrics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unilyze.Pipeline;

internal sealed record AnalysisResult(
    string ProjectPath,
    DateTimeOffset AnalyzedAt,
    IReadOnlyList<AssemblyInfo> Assemblies,
    IReadOnlyList<TypeNodeInfo> Types,
    IReadOnlyList<TypeDependency> Dependencies,
    IReadOnlyList<TypeMetrics>? TypeMetrics = null,
    string? AnalysisLevel = null,
    IReadOnlyList<CyclicDependency>? CyclicDependencies = null,
    int MetricsVersion = 0,
    string? ToolVersion = null,
    string? ProjectKind = null,
    string? Profile = null,
    int? SuppressedCount = null,
    IReadOnlyList<TypeApiSurface>? ApiSurface = null,
    bool? ResolveNuget = null,
    bool? IncludeGenerated = null,
    string? TargetFramework = null,
    double? EnergyPressure = null,
    int SchemaVersion = 0,
    IReadOnlyList<string>? SourceTable = null)
{
    /// <summary>
    /// Metric definition version written to JSON as <c>metricsVersion</c>.
    /// Increment when any change alters measured values (requires minor bump + CHANGELOG [metrics] entry).
    /// </summary>
    public const int CurrentMetricsVersion = 4;

    public const int CurrentSchemaVersion = 1;
}

internal sealed record AssemblyInfo(
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
        typeof(JsonStringEnumConverter<TypeRole>),
        typeof(JsonStringEnumConverter<ChangeStatus>),
        typeof(JsonStringEnumConverter<MethodChangeKind>),
        typeof(JsonStringEnumConverter<CycleLevel>)])]
[JsonSerializable(typeof(AnalysisResult))]
[JsonSerializable(typeof(DiffResult))]
[JsonSerializable(typeof(HotspotResult))]
[JsonSerializable(typeof(TrendResult))]
[JsonSerializable(typeof(QueryResult))]
[JsonSerializable(typeof(TypeEvidencePack))]
[JsonSerializable(typeof(TypeEvidenceMetrics))]
[JsonSerializable(typeof(TypeEvidenceSmell))]
[JsonSerializable(typeof(TypeEvidenceDependencyGroup))]
[JsonSerializable(typeof(TypeEvidenceMethod))]
[JsonSerializable(typeof(TypeApiSurface))]
[JsonSerializable(typeof(TypeEvidenceApiSurface))]
[JsonSerializable(typeof(MetricDelta<int>))]
[JsonSerializable(typeof(MetricDelta<double>))]
internal partial class AnalysisJsonContext : JsonSerializerContext;
