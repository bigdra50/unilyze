using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unilyze;

public sealed record AnalysisResult(
    string ProjectPath,
    DateTimeOffset AnalyzedAt,
    IReadOnlyList<AssemblyInfo> Assemblies,
    IReadOnlyList<TypeNodeInfo> Types,
    IReadOnlyList<TypeDependency> Dependencies,
    IReadOnlyList<TypeMetrics>? TypeMetrics = null);

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
    Converters = [typeof(JsonStringEnumConverter<DependencyKind>)])]
[JsonSerializable(typeof(AnalysisResult))]
internal partial class AnalysisJsonContext : JsonSerializerContext;
