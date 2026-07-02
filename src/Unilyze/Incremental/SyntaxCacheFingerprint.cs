using Unilyze.Discovery;
using Unilyze.Config;
using Unilyze.Cli;
using Unilyze.Pipeline;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Unilyze.Incremental;

internal static class SyntaxCacheFingerprint
{
    // v2: manifest gained per-assembly global-using hashes and the fingerprint folds in the
    // resolved reference set / TFM / analysis level for semantic-incremental keying.
    // v3: each file entry gained a per-file (non-global) using-directive hash, closing the hole
    // where a using retarget (e.g. an alias pointing at a different type) was invisible to
    // HasStructuralChange and fell through to the body-only fast path.
    // v4: each enriched type gained UsedTypes(T) (design doc §4.1/§4.2) — the in-source TypeIds
    // T's enrichment resolved, recorded via UsageRecorder for RDeps inversion in Phase A2.
    // Recording only in this schema version; invalidation is unchanged.
    public const int SchemaVersion = 4;

    public static string ComputeGlobalFingerprint(
        PipelineDiscoverState discover,
        AnalysisBuildOptions options,
        IReadOnlyList<AsmdefInfo> targets)
    {
        var config = options.EffectiveAnalysisConfig;
        var builder = new StringBuilder(512);
        builder.Append(ToolVersionInfo.Current).Append('\0');
        builder.Append(AnalysisResult.CurrentMetricsVersion).Append('\0');
        builder.AppendJoin('\0', discover.PreprocessorSymbols.OrderBy(s => s, StringComparer.Ordinal));
        builder.Append('\0');
        builder.Append(config.Profile).Append('\0');
        builder.Append(config.DisableCycles).Append('\0');
        builder.AppendJoin('\0', config.DisabledRuleKinds.OrderBy(k => k.ToString()));
        builder.Append('\0');
        builder.AppendJoin('\0', config.InformationalSmellKinds.OrderBy(k => k.ToString()));
        builder.Append('\0');
        builder.Append(SerializeSmellOverrides(config.SmellOverrides)).Append('\0');
        builder.Append(SerializeThresholds(config.Thresholds)).Append('\0');
        builder.Append(options.ExcludeGeneratedCode).Append('\0');
        builder.Append(options.ApplyAnyDepthExcludes).Append('\0');
        builder.AppendJoin('\0', (options.ExcludeDirectories ?? []).OrderBy(d => d, StringComparer.Ordinal));
        builder.Append('\0');
        builder.AppendJoin('\0', targets
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => $"{t.Name}|{Path.GetFullPath(t.Directory)}|{string.Join(',', t.ExcludeDirectories ?? [])}"));
        builder.Append('\0');
        // Semantic-level metrics (CBO/DIT/RFC and the smell detectors) bind against the
        // resolved reference set, the selected target framework, and the analysis level.
        // None of those are file-content signals, so a stale syntax-level manifest (or a
        // manifest built against a different reference set/TFM/level) must not be reused.
        builder.Append(SerializeReferences(discover.ResolvedReferences)).Append('\0');
        builder.Append(discover.SelectedTargetFramework ?? string.Empty).Append('\0');
        builder.Append(options.EffectiveCap);
        return HashUtf8(builder.ToString());
    }

    static string SerializeReferences(ResolvedDlls resolved)
    {
        var entries = resolved.Paths
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => $"{p}|{TryGetFileLength(p)}");
        return $"{resolved.Level}\0{string.Join('\0', entries)}";
    }

    static long TryGetFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    public static string HashFileContent(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string HashStrings(IEnumerable<string> values) =>
        HashUtf8(string.Join('\0', values));

    public static string HashKnownInterfaces(IReadOnlyList<TypeNodeInfo> rawTypes)
    {
        var names = rawTypes
            .Where(t => t.Kind == "interface")
            .Select(t => t.Name.Split('<')[0])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal);
        return HashUtf8(string.Join('\0', names));
    }

    static string SerializeSmellOverrides(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? smellOverrides)
    {
        if (smellOverrides is null || smellOverrides.Count == 0)
            return string.Empty;

        var ordered = smellOverrides
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => $"{kvp.Key}:{SerializeJsonObject(kvp.Value)}");
        return string.Join('\0', ordered);
    }

    static string SerializeThresholds(EffectiveSmellThresholds thresholds)
    {
        var entries = EffectiveSmellThresholds.BuildConfigEntries(thresholds)
            .Select(e => $"{e.Key}={e.Value}:{e.Overridden}");
        return string.Join('\0', entries);
    }

    static string SerializeJsonObject(IReadOnlyDictionary<string, JsonElement> values)
    {
        var ordered = values.OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => $"{kvp.Key}={kvp.Value.GetRawText()}");
        return string.Join(',', ordered);
    }

    static string HashUtf8(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
