using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unilyze;

internal sealed record BaselineFingerprintEntry(
    [property: JsonPropertyName("typeId")] string TypeId,
    [property: JsonPropertyName("kind")] CodeSmellKind Kind,
    [property: JsonPropertyName("methodName")] string? MethodName,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("maxSeverity")] SmellSeverity MaxSeverity);

internal sealed record BaselineFile(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("toolVersion")] string ToolVersion,
    [property: JsonPropertyName("metricsVersion")] int MetricsVersion,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("fingerprints")] IReadOnlyList<BaselineFingerprintEntry> Fingerprints)
{
    public const int CurrentSchemaVersion = 1;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter<CodeSmellKind>(), new JsonStringEnumConverter<SmellSeverity>() }
    };

    public static BaselineFile FromAnalysis(AnalysisResult result)
    {
        var groups = new Dictionary<string, (int Count, SmellSeverity MaxSeverity)>(StringComparer.Ordinal);

        if (result.TypeMetrics is not null)
        {
            foreach (var typeMetrics in result.TypeMetrics)
            {
                if (typeMetrics.CodeSmells is not { Count: > 0 })
                    continue;

                var typeId = TypeIdentity.GetTypeId(typeMetrics);
                foreach (var smell in typeMetrics.CodeSmells)
                {
                    var key = BuildKey(typeId, smell.Kind, smell.MethodName);
                    if (groups.TryGetValue(key, out var existing))
                    {
                        groups[key] = (
                            existing.Count + 1,
                            MaxSeverity(existing.MaxSeverity, smell.Severity));
                    }
                    else
                    {
                        groups[key] = (1, smell.Severity);
                    }
                }
            }
        }

        var fingerprints = groups
            .Select(pair => ParseKey(pair.Key, pair.Value.Count, pair.Value.MaxSeverity))
            .OrderBy(entry => entry.TypeId, StringComparer.Ordinal)
            .ThenBy(entry => entry.Kind)
            .ThenBy(entry => entry.MethodName ?? "", StringComparer.Ordinal)
            .ToList();

        return new BaselineFile(
            CurrentSchemaVersion,
            ToolVersionInfo.Current,
            AnalysisResult.CurrentMetricsVersion,
            DateTimeOffset.UtcNow,
            fingerprints);
    }

    public static BaselineFile Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BaselineFile>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to parse baseline file: {path}");
    }

    public static void Save(string path, BaselineFile baseline)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(baseline, JsonOptions);
        File.WriteAllText(path, json + Environment.NewLine);
    }

    public static string ResolvePath(string projectRoot, string baselinePath)
    {
        if (Path.IsPathRooted(baselinePath))
            return Path.GetFullPath(baselinePath);
        return Path.GetFullPath(Path.Combine(projectRoot, baselinePath));
    }

    public Dictionary<string, BaselineFingerprintEntry> ToLookup()
    {
        var lookup = new Dictionary<string, BaselineFingerprintEntry>(StringComparer.Ordinal);
        foreach (var entry in Fingerprints)
        {
            var key = BuildKey(entry.TypeId, entry.Kind, entry.MethodName);
            lookup[key] = entry;
        }
        return lookup;
    }

    internal static string BuildKey(string typeId, CodeSmellKind kind, string? methodName)
        => $"{typeId}|{SmellKey(kind, methodName)}";

    internal static string SmellKey(CodeSmellKind kind, string? methodName)
        => $"{kind}:{methodName ?? ""}";

    static BaselineFingerprintEntry ParseKey(string key, int count, SmellSeverity maxSeverity)
    {
        var separator = key.IndexOf('|');
        if (separator <= 0 || separator >= key.Length - 1)
            throw new InvalidOperationException($"Invalid baseline fingerprint key: '{key}'");

        var typeId = key[..separator];
        var smellKey = key[(separator + 1)..];
        var colon = smellKey.IndexOf(':');
        if (colon < 0)
            throw new InvalidOperationException($"Invalid baseline smell key: '{smellKey}'");

        var kindText = smellKey[..colon];
        if (!Enum.TryParse<CodeSmellKind>(kindText, out var kind))
            throw new InvalidOperationException($"Invalid baseline smell kind: '{kindText}'");

        var methodName = smellKey[(colon + 1)..];
        return new BaselineFingerprintEntry(
            typeId,
            kind,
            string.IsNullOrEmpty(methodName) ? null : methodName,
            count,
            maxSeverity);
    }

    static SmellSeverity MaxSeverity(SmellSeverity left, SmellSeverity right)
        => left == SmellSeverity.Critical || right == SmellSeverity.Critical
            ? SmellSeverity.Critical
            : SmellSeverity.Warning;
}

internal sealed record BaselineApplyStats(
    int SuppressedCount,
    int NewCount,
    int FixedEntryCount);

internal static class BaselineMatcher
{
    public static AnalysisResult Apply(AnalysisResult result, BaselineFile baseline, out BaselineApplyStats stats)
    {
        var lookup = baseline.ToLookup();
        var remaining = lookup.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal);
        var currentKeys = new HashSet<string>(StringComparer.Ordinal);

        var suppressedCount = 0;
        var newCount = 0;

        var updatedMetrics = result.TypeMetrics?.Select(typeMetrics =>
        {
            if (typeMetrics.CodeSmells is not { Count: > 0 })
                return typeMetrics;

            var typeId = TypeIdentity.GetTypeId(typeMetrics);
            var grouped = typeMetrics.CodeSmells
                .Select((smell, index) => (smell, index))
                .GroupBy(item => BaselineFile.BuildKey(typeId, item.smell.Kind, item.smell.MethodName),
                    StringComparer.Ordinal);

            foreach (var group in grouped)
                currentKeys.Add(group.Key);

            var updatedSmells = new List<CodeSmell>(typeMetrics.CodeSmells.Count);
            foreach (var group in grouped)
            {
                lookup.TryGetValue(group.Key, out var entry);
                var ordered = group
                    .OrderBy(item => item.smell.Line ?? int.MaxValue)
                    .ThenBy(item => item.smell.Message, StringComparer.Ordinal)
                    .ThenBy(item => item.index)
                    .Select(item => item.smell)
                    .ToList();

                foreach (var smell in ordered)
                {
                    if (entry is null
                        || !remaining.TryGetValue(group.Key, out var budget)
                        || budget <= 0
                        || IsSeverityEscalation(smell.Severity, entry.MaxSeverity))
                    {
                        updatedSmells.Add(smell);
                        newCount++;
                        continue;
                    }

                    updatedSmells.Add(smell with { Baselined = true });
                    remaining[group.Key] = budget - 1;
                    suppressedCount++;
                }
            }

            return typeMetrics with { CodeSmells = updatedSmells };
        }).ToList();

        var fixedEntryCount = lookup.Keys.Count(key => !currentKeys.Contains(key));
        stats = new BaselineApplyStats(suppressedCount, newCount, fixedEntryCount);

        return result with
        {
            TypeMetrics = updatedMetrics,
            SuppressedCount = suppressedCount,
        };
    }

    static bool IsSeverityEscalation(SmellSeverity current, SmellSeverity baselineMax)
        => current == SmellSeverity.Critical && baselineMax == SmellSeverity.Warning;

    public static void WarnIfMetricsVersionMismatch(BaselineFile baseline)
    {
        if (baseline.MetricsVersion == AnalysisResult.CurrentMetricsVersion)
            return;

        Console.Error.WriteLine(
            $"Warning: baseline metricsVersion ({ToolVersionInfo.FormatMetricsVersion(baseline.MetricsVersion)}) "
            + $"differs from current ({ToolVersionInfo.FormatMetricsVersion(AnalysisResult.CurrentMetricsVersion)}). "
            + "Baseline matches may be unreliable; consider re-running 'unilyze baseline create'.");
    }

    public static void WriteSummary(BaselineApplyStats stats)
    {
        Console.Error.WriteLine(
            $"Baseline: {stats.SuppressedCount} suppressed, {stats.NewCount} new smell(s).");
        if (stats.FixedEntryCount > 0)
        {
            Console.Error.WriteLine(
                $"Baseline: {stats.FixedEntryCount} fingerprint(s) no longer match any smell (fixed); "
                + "re-run 'unilyze baseline create' to ratchet down.");
        }
    }
}
