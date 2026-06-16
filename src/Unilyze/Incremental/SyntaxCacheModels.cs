using Unilyze.Detectors;
using Unilyze.Metrics;
using Unilyze.Pipeline;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;

namespace Unilyze.Incremental;

internal sealed record SyntaxCacheManifest(
    int SchemaVersion,
    string Fingerprint,
    IReadOnlyDictionary<string, string> KnownInterfacesHashesByAssembly,
    IReadOnlyList<SyntaxCacheFileEntry> Files,
    IReadOnlyDictionary<string, string>? GlobalUsingsHashesByAssembly = null);

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
    SyntaxCacheManifest ManifestDraft,
    // When true a structural change (signature/type-set/global-using/file add or delete) means
    // the body-only fast path is unsafe at a semantic level, so every type is re-enriched.
    bool RequiresFullReEnrich = false);

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
