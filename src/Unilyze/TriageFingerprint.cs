using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unilyze;

internal sealed record TriageEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("verdict")] string Verdict,
    [property: JsonPropertyName("reason")] string? Reason = null,
    [property: JsonPropertyName("by")] string? By = null,
    [property: JsonPropertyName("createdAt")] DateTimeOffset? CreatedAt = null);

internal sealed record TriageFile(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("toolVersion")] string ToolVersion,
    [property: JsonPropertyName("metricsVersion")] int MetricsVersion,
    [property: JsonPropertyName("entries")] IReadOnlyList<TriageEntry> Entries)
{
    public const int CurrentSchemaVersion = 1;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static TriageFile CreateEmpty()
        => new(
            CurrentSchemaVersion,
            ToolVersionInfo.Current,
            AnalysisResult.CurrentMetricsVersion,
            []);

    public static TriageFile Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TriageFile>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to parse triage file: {path}");
    }

    public static void Save(string path, TriageFile triage)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(triage, JsonOptions);
        File.WriteAllText(path, json + Environment.NewLine);
    }

    public static string ResolvePath(string projectRoot, string triagePath)
    {
        if (Path.IsPathRooted(triagePath))
            return Path.GetFullPath(triagePath);
        return Path.GetFullPath(Path.Combine(projectRoot, triagePath));
    }

    public static string DefaultPath(string projectRoot)
        => Path.Combine(projectRoot, ".unilyze", "triage.json");

    public Dictionary<string, TriageEntry> ToLookup()
    {
        var lookup = new Dictionary<string, TriageEntry>(StringComparer.Ordinal);
        foreach (var entry in Entries)
            lookup[entry.Id] = entry;
        return lookup;
    }

    public TriageFile Upsert(TriageEntry entry)
    {
        var updated = Entries
            .Where(existing => !string.Equals(existing.Id, entry.Id, StringComparison.Ordinal))
            .Append(entry)
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
        return this with { Entries = updated };
    }

    public TriageFile RemoveStale(IReadOnlySet<string> currentIds)
    {
        var pruned = Entries.Where(entry => currentIds.Contains(entry.Id)).ToList();
        return this with { Entries = pruned };
    }
}

internal sealed record TriageApplyStats(
    int MatchedCount,
    int ConfirmedCount,
    int FalsePositiveCount,
    int WontFixCount,
    int StaleCount);

internal static class TriageMatcher
{
    public static AnalysisResult Apply(AnalysisResult result, TriageFile triage, out TriageApplyStats stats)
    {
        var lookup = triage.ToLookup();
        var currentIds = new HashSet<string>(StringComparer.Ordinal);
        var matched = new Dictionary<string, int>(StringComparer.Ordinal);

        var updatedMetrics = result.TypeMetrics?.Select(typeMetrics =>
        {
            if (typeMetrics.CodeSmells is not { Count: > 0 })
                return typeMetrics;

            var updatedSmells = typeMetrics.CodeSmells.Select(smell =>
            {
                if (smell.Id is null || !lookup.TryGetValue(smell.Id, out var entry))
                    return smell;

                currentIds.Add(smell.Id);
                matched.TryGetValue(entry.Verdict, out var count);
                matched[entry.Verdict] = count + 1;
                return smell with { Triage = entry.Verdict };
            }).ToList();

            return typeMetrics with { CodeSmells = updatedSmells };
        }).ToList();

        var staleCount = lookup.Keys.Count(id => !currentIds.Contains(id));
        stats = new TriageApplyStats(
            matched.Values.Sum(),
            matched.GetValueOrDefault(TriageVerdicts.Confirmed),
            matched.GetValueOrDefault(TriageVerdicts.FalsePositive),
            matched.GetValueOrDefault(TriageVerdicts.WontFix),
            staleCount);

        return result with { TypeMetrics = updatedMetrics };
    }

    public static HashSet<string> CollectCurrentIds(AnalysisResult result)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (result.TypeMetrics is null)
            return ids;

        foreach (var typeMetrics in result.TypeMetrics)
        {
            if (typeMetrics.CodeSmells is not { Count: > 0 })
                continue;
            foreach (var smell in typeMetrics.CodeSmells)
            {
                if (!string.IsNullOrEmpty(smell.Id))
                    ids.Add(smell.Id);
            }
        }

        return ids;
    }

    public static void WarnIfMetricsVersionMismatch(TriageFile triage)
    {
        if (triage.MetricsVersion == AnalysisResult.CurrentMetricsVersion)
            return;

        Console.Error.WriteLine(
            $"Warning: triage metricsVersion ({ToolVersionInfo.FormatMetricsVersion(triage.MetricsVersion)}) "
            + $"differs from current ({ToolVersionInfo.FormatMetricsVersion(AnalysisResult.CurrentMetricsVersion)}). "
            + "Triage matches may be unreliable.");
    }

    public static void WriteSummary(TriageApplyStats stats)
    {
        if (stats.MatchedCount == 0)
            return;

        Console.Error.WriteLine(
            $"Triage: {stats.MatchedCount} matched "
            + $"(confirmed={stats.ConfirmedCount}, false-positive={stats.FalsePositiveCount}, wontfix={stats.WontFixCount}).");
        if (stats.StaleCount > 0)
        {
            Console.Error.WriteLine(
                $"Triage: {stats.StaleCount} stale verdict(s) no longer match any finding; "
                + "run 'unilyze triage prune' to remove.");
        }
    }
}
