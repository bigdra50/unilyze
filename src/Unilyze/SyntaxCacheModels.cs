using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;

namespace Unilyze;

internal sealed record SyntaxCacheManifest(
    int SchemaVersion,
    string Fingerprint,
    IReadOnlyDictionary<string, string> KnownInterfacesHashesByAssembly,
    IReadOnlyList<SyntaxCacheFileEntry> Files);

internal sealed record SyntaxCacheFileEntry(
    string RelativePath,
    string ContentHash,
    string Assembly,
    IReadOnlyList<TypeNodeInfo> RawTypes,
    IReadOnlyList<SyntaxCacheEnrichedType> EnrichedTypes);

internal sealed record SyntaxCacheEnrichedType(
    string TypeId,
    TypeMetrics Metrics);

internal sealed record SyntaxIncrementalCollectResult(
    List<TypeNodeInfo> Types,
    List<SyntaxTree> SyntaxTrees,
    IReadOnlyDictionary<string, IReadOnlyList<TypeNodeInfo>> RawTypesByFile,
    IReadOnlyDictionary<string, SyntaxCacheEnrichedType> CachedEnrichmentByTypeId,
    IReadOnlySet<string> ReparsedFiles,
    SyntaxCacheManifest ManifestDraft);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [
        typeof(JsonStringEnumConverter<DependencyKind>),
        typeof(JsonStringEnumConverter<CodeSmellKind>),
        typeof(JsonStringEnumConverter<SmellSeverity>),
        typeof(JsonStringEnumConverter<TypeRole>)])]
[JsonSerializable(typeof(SyntaxCacheManifest))]
[JsonSerializable(typeof(SyntaxCacheFileEntry))]
[JsonSerializable(typeof(SyntaxCacheEnrichedType))]
[JsonSerializable(typeof(TypeNodeInfo))]
[JsonSerializable(typeof(TypeMetrics))]
[JsonSerializable(typeof(MemberInfo))]
[JsonSerializable(typeof(ParameterInfo))]
[JsonSerializable(typeof(AttributeInfo))]
[JsonSerializable(typeof(GenericConstraintInfo))]
[JsonSerializable(typeof(MethodMetrics))]
[JsonSerializable(typeof(CodeSmell))]
[JsonSerializable(typeof(DetectedSmell))]
internal partial class SyntaxCacheJsonContext : JsonSerializerContext;
