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
    IReadOnlyList<SyntaxCacheEnrichedType> EnrichedTypes,
    // Hash of the file's normalized, order-insensitive, non-global using-directive set
    // (regular/static/alias). Lets HasStructuralChange catch a per-file using retarget —
    // e.g. an alias pointing at a different type — that FileStructureChanged cannot see
    // because it never looks past the type/member declaration shape.
    string UsingsHash = "");

internal sealed record SyntaxCacheEnrichedType(
    string TypeId,
    TypeMetrics Metrics,
    // UsedTypes(T) (design doc §4.1): TypeIds this type's enrichment actually resolved (base
    // chain, interfaces, member signature types, attribute/constraint types, file using-static/
    // alias targets, and every bound symbol's containing type from one IOperation walk per
    // member body). Ordinal-sorted for stable manifest diffs. Recording only — Phase A2 inverts
    // this into RDeps(B) for precise invalidation; nothing consumes it yet.
    IReadOnlyList<string> UsedTypes);

internal sealed record SyntaxIncrementalCollectResult(
    List<TypeNodeInfo> Types,
    List<SyntaxTree> SyntaxTrees,
    IReadOnlyDictionary<string, IReadOnlyList<TypeNodeInfo>> RawTypesByFile,
    IReadOnlyDictionary<string, SyntaxCacheEnrichedType> CachedEnrichmentByTypeId,
    IReadOnlySet<string> ReparsedFiles,
    SyntaxCacheManifest ManifestDraft,
    // When true, a change that still has no precise (RDeps-based) invalidation rule — a type or
    // file added/removed, a member added/removed, a base/interface change, or a global using
    // change — means every type is re-enriched (design doc §4.3). Δsig(B) and Δusing(F) no
    // longer set this; they resolve through PreciseExtraReEnrichTypeIds instead.
    bool RequiresFullReEnrich = false,
    // Per-file using-directive hash (absolute path keyed), carried over from cache for
    // untouched files and freshly computed for reparsed ones. BuildManifest persists these
    // alongside each SyntaxCacheFileEntry so the next generation can diff them.
    IReadOnlyDictionary<string, string>? UsingsHashByFile = null,
    // Δsig(B) ∪ Δusing(F) precise invalidation (design doc §4.3): TypeIds, EXTRA beyond SEED
    // (the reparsed files' own types + partial closure), that must also be re-enriched —
    // RDeps(B) for each signature-changed B, and RDeps(F's types) for each using-changed file F.
    // Ignored when RequiresFullReEnrich is true. Empty (not null) when nothing beyond SEED is
    // needed, i.e. the ordinary body-only fast path.
    IReadOnlySet<string>? PreciseExtraReEnrichTypeIds = null,
    // Diagnostic suffix appended to the "[incremental] re-enrich types: n/m" log line when the
    // precise path contributed anything beyond SEED, e.g. "(rdi: sig=1 using=0)". Null otherwise.
    string? PreciseLogSuffix = null);

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
