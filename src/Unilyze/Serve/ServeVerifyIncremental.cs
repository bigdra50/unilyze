using Unilyze.Pipeline;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Unilyze.Serve;

/// <summary>
/// Shadow verification (design doc §7.3, tasks/reverse-dependency-index-design.md): compares a
/// full (non-incremental) analysis result against the primary incremental one and reports which
/// types diverge. A pure comparison over two already-computed <see cref="AnalysisResult"/>
/// instances — it does not run analysis itself and never throws on a genuine divergence (only a
/// malformed JSON payload would), so the caller decides what "diverged" means for its context
/// (SnapshotBuilder logs it and keeps serving the incremental result regardless).
/// </summary>
internal static class ServeVerifyIncremental
{
    public readonly record struct DivergenceReport(bool Diverged, IReadOnlyList<string> TypeIds);

    static readonly DivergenceReport NoDivergence = new(false, []);

    public static DivergenceReport Compare(AnalysisResult full, AnalysisResult incremental)
    {
        var fullNode = Normalize(full);
        var incrementalNode = Normalize(incremental);
        if (JsonNode.DeepEquals(fullNode, incrementalNode))
            return NoDivergence;

        var divergentTypeIds = DiffTypeMetrics(fullNode, incrementalNode);
        return new DivergenceReport(true, divergentTypeIds);
    }

    // Minimal src-side counterpart to the test suite's IncrementalCliHelper.Normalize (design doc
    // §7.3: "existing Normalize equivalent logic, minimal implementation on the src side"):
    // strips run-to-run noise (analysis timestamp, tool version) that differs between any two
    // runs regardless of correctness. Unlike the test helper, this does not need to resolve the
    // sourceTable/fileRef indirection back to plain paths — both sides are freshly built from the
    // SAME source tree in the SAME process, via the same file-enumeration order, so their
    // sourceTable entries are already identical; only the two noise fields need stripping.
    static JsonObject Normalize(AnalysisResult result)
    {
        var json = JsonSerializer.Serialize(result, AnalysisJsonContext.Default.AnalysisResult);
        var node = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Invalid AnalysisResult JSON");
        node.Remove("analyzedAt");
        node.Remove("toolVersion");
        return node;
    }

    // design doc §4.4: RDI narrows only the SemanticEnricher.Enrich subset (typeMetrics); the
    // declaration graph, dependencies, and aggregation stay full every generation. So a
    // divergence traceable to specific TypeIds is almost always a typeMetrics disagreement —
    // diff there first for an actionable TypeId list. Falls back to a generic message if the
    // divergence lives elsewhere (types/dependencies/aggregation), which RDI never touches but a
    // bug outside RDI's scope still could.
    static IReadOnlyList<string> DiffTypeMetrics(JsonObject fullNode, JsonObject incrementalNode)
    {
        var fullByTypeId = IndexByTypeId(fullNode["typeMetrics"] as JsonArray);
        var incrementalByTypeId = IndexByTypeId(incrementalNode["typeMetrics"] as JsonArray);

        var divergent = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var typeId in fullByTypeId.Keys.Concat(incrementalByTypeId.Keys).Distinct(StringComparer.Ordinal))
        {
            var inFull = fullByTypeId.TryGetValue(typeId, out var fullEntry);
            var inIncremental = incrementalByTypeId.TryGetValue(typeId, out var incrementalEntry);
            if (!inFull || !inIncremental || !JsonNode.DeepEquals(fullEntry, incrementalEntry))
                divergent.Add(typeId);
        }

        return divergent.Count > 0
            ? [.. divergent]
            : ["(non-type-keyed divergence outside typeMetrics — see full snapshot diff)"];
    }

    static Dictionary<string, JsonNode?> IndexByTypeId(JsonArray? typeMetrics)
    {
        var result = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (typeMetrics is null)
            return result;

        foreach (var entry in typeMetrics)
        {
            if (entry?["typeId"]?.GetValue<string>() is { } typeId)
                result[typeId] = entry;
        }

        return result;
    }
}
