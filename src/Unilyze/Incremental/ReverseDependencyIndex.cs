namespace Unilyze.Incremental;

// Inverts UsageRecorder's per-type UsedTypes(T) into RDeps(B) = {T | B ∈ UsedTypes(T)} (design
// doc §4.2-4.3): "who used B" for a structural change to B. Built from the OLD (cached) manifest's
// EnrichedTypes, not the freshly reparsed one — invalidation asks who resolved B's PREVIOUS
// surface, since that is exactly whose cached enrichment can now be stale. A pure function over
// the manifest's file entries so it stays unit-testable without spinning up a real analysis.
internal static class ReverseDependencyIndex
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Build(
        IEnumerable<SyntaxCacheFileEntry> files)
    {
        var rdeps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var enriched in file.EnrichedTypes)
            {
                foreach (var usedTypeId in enriched.UsedTypes)
                {
                    if (!rdeps.TryGetValue(usedTypeId, out var dependents))
                    {
                        dependents = [];
                        rdeps[usedTypeId] = dependents;
                    }
                    dependents.Add(enriched.TypeId);
                }
            }
        }

        return rdeps.ToDictionary(
            kvp => kvp.Key, IReadOnlyList<string> (kvp) => kvp.Value, StringComparer.Ordinal);
    }

    // RDeps(B) for a single B, or empty when nothing recorded a use of B (never referenced, or B
    // is new — the caller's Δadd/Δdel full fallback covers that case separately).
    public static IReadOnlyList<string> Resolve(
        IReadOnlyDictionary<string, IReadOnlyList<string>> rdeps, string typeId) =>
        rdeps.TryGetValue(typeId, out var dependents) ? dependents : [];

    // Union of RDeps(b) over every b in `typeIds` — e.g. RDeps(Δsig types) or RDeps(a file's
    // declared types) for Δusing(F).
    public static IReadOnlySet<string> ResolveMany(
        IReadOnlyDictionary<string, IReadOnlyList<string>> rdeps, IEnumerable<string> typeIds)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var typeId in typeIds)
            if (rdeps.TryGetValue(typeId, out var dependents))
                result.UnionWith(dependents);
        return result;
    }
}
