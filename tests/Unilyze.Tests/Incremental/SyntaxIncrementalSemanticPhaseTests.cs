using Unilyze.Incremental;
using Unilyze.Pipeline;

namespace Unilyze.Tests.Incremental;

// DetermineTypesToReEnrich's collapse-to-full behavior (design doc §4.3): once SEED ∪ precise
// RDI extras exceeds CollapseThresholdRatio of all types, precise bookkeeping stops paying for
// itself and the generation falls back to full (same correctness, cheaper accounting).
public sealed class SyntaxIncrementalSemanticPhaseTests
{
    [Fact]
    public void PreciseSetExceedingThreshold_CollapsesToFull()
    {
        var allTypes = BuildTypes(10);
        var seedFile = allTypes[0].FilePath;
        // SEED = {T0} (reparsed) + 6 precise extras = 7/10 = 70% > 60% threshold.
        var extra = new HashSet<string>(
            allTypes.Skip(1).Take(6).Select(TypeIdentity.GetTypeId), StringComparer.Ordinal);
        var collect = BuildCollect(allTypes, seedFile, extra, "(rdi: sig=6 using=0)");
        var log = new RecordingLogSink();

        var (typeIds, suffix) = SyntaxIncrementalSemanticPhase.DetermineTypesToReEnrich(allTypes, collect, log);

        Assert.Equal(allTypes.Count, typeIds.Count);
        Assert.Null(suffix);
        Assert.Contains(log.InfoMessages, m => m.Contains("collapse threshold", StringComparison.Ordinal));
    }

    [Fact]
    public void PreciseSetBelowThreshold_StaysPrecise()
    {
        var allTypes = BuildTypes(10);
        var seedFile = allTypes[0].FilePath;
        // SEED = {T0} (reparsed) + 2 precise extras = 3/10 = 30% < 60% threshold.
        var extra = new HashSet<string>(
            allTypes.Skip(1).Take(2).Select(TypeIdentity.GetTypeId), StringComparer.Ordinal);
        var collect = BuildCollect(allTypes, seedFile, extra, "(rdi: sig=2 using=0)");
        var log = new RecordingLogSink();

        var (typeIds, suffix) = SyntaxIncrementalSemanticPhase.DetermineTypesToReEnrich(allTypes, collect, log);

        Assert.Equal(3, typeIds.Count);
        Assert.Equal("(rdi: sig=2 using=0)", suffix);
        Assert.DoesNotContain(log.InfoMessages, m => m.Contains("collapse", StringComparison.Ordinal));
    }

    // A pure body-only bulk edit (many files reparsed, zero structural deltas → no
    // PreciseLogSuffix) must never collapse: SEED-only is v1's proven fast path, and collapsing
    // it to full would regress large structurally-clean edits that v1 handled with SEED alone.
    [Fact]
    public void BodyOnlyBulkSeedAboveThreshold_NeverCollapses()
    {
        var allTypes = BuildTypes(10);
        // SEED = 8/10 = 80% > 60% threshold, but the precise path was never engaged.
        var reparsed = allTypes.Take(8).Select(t => t.FilePath).ToHashSet(StringComparer.Ordinal);
        var collect = new SyntaxIncrementalCollectResult(
            Types: allTypes,
            SyntaxTrees: [],
            RawTypesByFile: new Dictionary<string, IReadOnlyList<TypeNodeInfo>>(StringComparer.Ordinal),
            CachedEnrichmentByTypeId: new Dictionary<string, SyntaxCacheEnrichedType>(StringComparer.Ordinal),
            ReparsedFiles: reparsed,
            ManifestDraft: SampleManifestDraft(),
            RequiresFullReEnrich: false,
            UsingsHashByFile: null,
            PreciseExtraReEnrichTypeIds: null,
            PreciseLogSuffix: null);
        var log = new RecordingLogSink();

        var (typeIds, suffix) = SyntaxIncrementalSemanticPhase.DetermineTypesToReEnrich(allTypes, collect, log);

        Assert.Equal(8, typeIds.Count);
        Assert.Null(suffix);
        Assert.DoesNotContain(log.InfoMessages, m => m.Contains("collapse", StringComparison.Ordinal));
    }

    [Fact]
    public void RequiresFullReEnrich_IgnoresPreciseExtrasAndReturnsEveryType()
    {
        var allTypes = BuildTypes(5);
        var collect = new SyntaxIncrementalCollectResult(
            Types: allTypes,
            SyntaxTrees: [],
            RawTypesByFile: new Dictionary<string, IReadOnlyList<TypeNodeInfo>>(StringComparer.Ordinal),
            CachedEnrichmentByTypeId: new Dictionary<string, SyntaxCacheEnrichedType>(StringComparer.Ordinal),
            ReparsedFiles: new HashSet<string>(StringComparer.Ordinal),
            ManifestDraft: SampleManifestDraft(),
            RequiresFullReEnrich: true);
        var log = new RecordingLogSink();

        var (typeIds, suffix) = SyntaxIncrementalSemanticPhase.DetermineTypesToReEnrich(allTypes, collect, log);

        Assert.Equal(allTypes.Count, typeIds.Count);
        Assert.Null(suffix);
        Assert.Empty(log.InfoMessages);
    }

    static List<TypeNodeInfo> BuildTypes(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new TypeNodeInfo(
                Name: $"T{i}", Namespace: "Sample", Kind: "class", Modifiers: ["public"],
                BaseType: null, Interfaces: [], Members: [], ConstructorParams: [],
                Attributes: [], GenericConstraints: [], EnumBaseType: null,
                Assembly: "Asm", FilePath: $"/src/T{i}.cs", IsNested: false))
            .ToList();

    static SyntaxIncrementalCollectResult BuildCollect(
        List<TypeNodeInfo> allTypes, string seedFile, IReadOnlySet<string> preciseExtra, string suffix) =>
        new(
            Types: allTypes,
            SyntaxTrees: [],
            RawTypesByFile: new Dictionary<string, IReadOnlyList<TypeNodeInfo>>(StringComparer.Ordinal),
            CachedEnrichmentByTypeId: new Dictionary<string, SyntaxCacheEnrichedType>(StringComparer.Ordinal),
            ReparsedFiles: new HashSet<string>(StringComparer.Ordinal) { seedFile },
            ManifestDraft: SampleManifestDraft(),
            RequiresFullReEnrich: false,
            UsingsHashByFile: null,
            PreciseExtraReEnrichTypeIds: preciseExtra,
            PreciseLogSuffix: suffix);

    static SyntaxCacheManifest SampleManifestDraft() =>
        new(1, "fingerprint", new Dictionary<string, string>(StringComparer.Ordinal), []);

    sealed class RecordingLogSink : IAnalysisLogSink
    {
        public List<string> InfoMessages { get; } = [];
        public void Info(string message) => InfoMessages.Add(message);
        public void Warning(string message) { }
        public void PhaseStarted(string phase) { }
        public void PhaseCompleted(string phase, TimeSpan elapsed) { }
    }
}
