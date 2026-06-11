using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unilyze;

internal readonly record struct ResolvedAnalysisConfig(
    EffectiveSmellThresholds Thresholds,
    string Profile,
    IReadOnlySet<CodeSmellKind> DisabledRuleKinds,
    bool DisableCycles,
    IReadOnlySet<CodeSmellKind> InformationalSmellKinds,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? SmellOverrides);

internal sealed record UnilyzeConfig(
    [property: JsonPropertyName("excludeDirs")]
    IReadOnlyList<string>? ExcludeDirs = null,
    [property: JsonPropertyName("disableDefaultExcludes")]
    bool DisableDefaultExcludes = false,
    [property: JsonPropertyName("disableGeneratedCodeExcludes")]
    bool DisableGeneratedCodeExcludes = false,
    [property: JsonPropertyName("smells")]
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? Smells = null,
    [property: JsonPropertyName("rules")]
    IReadOnlyDictionary<string, string>? Rules = null,
    [property: JsonPropertyName("profile")]
    string? Profile = null,
    [property: JsonPropertyName("baseline")]
    string? Baseline = null,
    [property: JsonPropertyName("triage")]
    string? Triage = null,
    [property: JsonPropertyName("maxParallelism")]
    int? MaxParallelism = null)
{
    public static UnilyzeConfig Empty { get; } = new();

    internal static int ResolveMaxParallelism(int? configValue) =>
        configValue is > 0 ? configValue.Value : Environment.ProcessorCount;

    static readonly IReadOnlySet<CodeSmellKind> NoDisabledRules =
        new HashSet<CodeSmellKind>();

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    public static UnilyzeConfig LoadMerged(
        string projectRoot,
        IReadOnlyList<string>? cliExcludeDirs = null,
        string? cliProfile = null)
    {
        var global = LoadFile(GetGlobalConfigPath());
        var project = LoadFile(GetProjectConfigPath(projectRoot));
        var merged = Merge(global, project);

        if (cliExcludeDirs is { Count: > 0 })
            merged = Merge(merged, new UnilyzeConfig(cliExcludeDirs));

        if (cliProfile is not null)
            merged = merged with { Profile = cliProfile };

        var resolved = BuildEffectiveExcludeDirs(merged, projectRoot);
        return merged with { ExcludeDirs = resolved };
    }

    internal static IReadOnlyList<string>? BuildEffectiveExcludeDirs(UnilyzeConfig config, string projectRoot)
    {
        var resolved = CollectDefaultExcludeDirs(config, projectRoot);
        AppendUserExcludeDirs(resolved, config, projectRoot);
        return resolved.Count > 0 ? resolved : null;
    }

    static List<string> CollectDefaultExcludeDirs(UnilyzeConfig config, string projectRoot)
    {
        if (config.DisableDefaultExcludes)
            return [];

        return DefaultExcludes.ResolveProjectPaths(projectRoot).ToList();
    }

    static void AppendUserExcludeDirs(List<string> resolved, UnilyzeConfig config, string projectRoot)
    {
        if (config.ExcludeDirs is not { Count: > 0 })
            return;

        foreach (var dir in ResolveExcludePaths(config.ExcludeDirs, projectRoot))
        {
            if (resolved.Any(existing => existing.Equals(dir, StringComparison.OrdinalIgnoreCase)))
                continue;
            resolved.Add(dir);
        }
    }

    internal static UnilyzeConfig LoadFile(string path)
    {
        if (!File.Exists(path))
            return Empty;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UnilyzeConfig>(json, JsonOptions) ?? Empty;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: Failed to load config {path}: {ex.Message}");
            return Empty;
        }
    }

    internal static UnilyzeConfig Merge(UnilyzeConfig lower, UnilyzeConfig higher)
    {
        return new UnilyzeConfig(
            MergeExcludeDirs(lower.ExcludeDirs, higher.ExcludeDirs),
            lower.DisableDefaultExcludes || higher.DisableDefaultExcludes,
            lower.DisableGeneratedCodeExcludes || higher.DisableGeneratedCodeExcludes,
            MergeSmells(lower.Smells, higher.Smells),
            MergeRules(lower.Rules, higher.Rules),
            higher.Profile ?? lower.Profile,
            higher.Baseline ?? lower.Baseline,
            higher.Triage ?? lower.Triage,
            higher.MaxParallelism ?? lower.MaxParallelism);
    }

    static IReadOnlyList<string>? MergeExcludeDirs(
        IReadOnlyList<string>? lower,
        IReadOnlyList<string>? higher)
    {
        if (lower is not { Count: > 0 })
            return higher;
        if (higher is not { Count: > 0 })
            return lower;

        var merged = new HashSet<string>(lower, StringComparer.OrdinalIgnoreCase);
        foreach (var dir in higher)
            merged.Add(dir);
        return merged.ToList();
    }

    internal ResolvedAnalysisConfig ResolveAnalysisConfig()
    {
        var profile = SmellThresholdProfiles.NormalizeProfile(Profile);
        if (!SmellThresholdProfiles.IsKnownProfile(profile))
        {
            Console.Error.WriteLine(
                $"Warning: Unknown profile '{Profile}'; using '{SmellThresholdProfiles.DefaultProfileName}'.");
            profile = SmellThresholdProfiles.DefaultProfileName;
        }

        var thresholds = SmellThresholdProfiles.ResolveEffectiveThresholds(
            profile, TypeRole.PlainCSharp, Smells);
        var disabledRuleKinds = ResolveDisabledRuleKinds(Rules, out var disableCycles);

        return new ResolvedAnalysisConfig(
            thresholds,
            profile,
            disabledRuleKinds,
            disableCycles,
            SmellThresholdProfiles.GetInformationalSmellKinds(profile),
            Smells);
    }

    static IReadOnlySet<CodeSmellKind> ResolveDisabledRuleKinds(
        IReadOnlyDictionary<string, string>? rules,
        out bool disableCycles)
    {
        disableCycles = false;
        if (rules is not { Count: > 0 })
            return NoDisabledRules;

        var disabled = new HashSet<CodeSmellKind>();
        foreach (var (ruleId, state) in rules)
            ApplyRuleState(ruleId, state, disabled, ref disableCycles);

        return disabled.Count > 0 ? disabled : NoDisabledRules;
    }

    static void ApplyRuleState(
        string ruleId,
        string state,
        HashSet<CodeSmellKind> disabled,
        ref bool disableCycles)
    {
        if (!string.Equals(state, "off", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(state, "on", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    $"Warning: Unknown rule state '{state}' for '{ruleId}'; expected 'on' or 'off'. Ignoring.");
            }
            return;
        }

        if (string.Equals(ruleId, "UNI009", StringComparison.OrdinalIgnoreCase))
        {
            disableCycles = true;
            return;
        }

        if (SarifFormatter.TryGetKind(ruleId, out var kind))
            disabled.Add(kind);
        else
            Console.Error.WriteLine($"Warning: Unknown rule id '{ruleId}' in config; ignoring.");
    }

    static IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? MergeSmells(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? lower,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? higher)
    {
        if (lower is not { Count: > 0 })
            return higher;
        if (higher is not { Count: > 0 })
            return lower;

        var merged = new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(
            lower, StringComparer.OrdinalIgnoreCase);
        foreach (var (smellName, higherInner) in higher)
            merged[smellName] = MergeSmellOverrides(merged, smellName, higherInner);

        return merged;
    }

    static IReadOnlyDictionary<string, JsonElement> MergeSmellOverrides(
        Dictionary<string, IReadOnlyDictionary<string, JsonElement>> merged,
        string smellName,
        IReadOnlyDictionary<string, JsonElement> higherInner)
    {
        if (!merged.TryGetValue(smellName, out var lowerInner))
            return higherInner;

        var inner = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in lowerInner)
            inner[key] = value;
        foreach (var (key, value) in higherInner)
            inner[key] = value;
        return inner;
    }

    static IReadOnlyDictionary<string, string>? MergeRules(
        IReadOnlyDictionary<string, string>? lower,
        IReadOnlyDictionary<string, string>? higher)
    {
        if (lower is not { Count: > 0 })
            return higher;
        if (higher is not { Count: > 0 })
            return lower;

        var merged = new Dictionary<string, string>(lower, StringComparer.OrdinalIgnoreCase);
        foreach (var (ruleId, state) in higher)
            merged[ruleId] = state;
        return merged;
    }

    internal static IReadOnlyList<string> ResolveExcludePaths(
        IReadOnlyList<string> excludeDirs, string projectRoot)
    {
        var resolved = new List<string>(excludeDirs.Count);
        foreach (var dir in excludeDirs)
        {
            var full = Path.IsPathRooted(dir)
                ? Path.GetFullPath(dir)
                : Path.GetFullPath(Path.Combine(projectRoot, dir));
            resolved.Add(full);
        }
        return resolved;
    }

    static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static void SaveFile(string path, UnilyzeConfig config)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, WriteOptions);
        File.WriteAllText(path, json + Environment.NewLine);
    }

    internal static bool AddExcludeDir(string configPath, string dir)
    {
        var config = LoadFile(configPath);
        var existing = config.ExcludeDirs?.ToList() ?? [];

        if (existing.Any(e => e.Equals(dir, StringComparison.OrdinalIgnoreCase)))
            return false;

        existing.Add(dir);
        SaveFile(configPath, config with { ExcludeDirs = existing });
        return true;
    }

    internal static bool RemoveExcludeDir(string configPath, string dir)
    {
        var config = LoadFile(configPath);
        if (config.ExcludeDirs is not { Count: > 0 })
            return false;

        if (!TryRemoveExcludeDir(config.ExcludeDirs, dir, out var updated))
            return false;

        SaveFile(configPath, config with { ExcludeDirs = updated });
        return true;
    }

    static bool TryRemoveExcludeDir(
        IReadOnlyList<string> excludeDirs,
        string dir,
        out IReadOnlyList<string>? updated)
    {
        var filtered = excludeDirs
            .Where(e => !e.Equals(dir, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filtered.Count == excludeDirs.Count)
        {
            updated = null;
            return false;
        }

        updated = filtered.Count > 0 ? filtered : null;
        return true;
    }

    internal static string GetGlobalConfigPath()
    {
        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(xdgConfig))
            xdgConfig = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        return Path.Combine(xdgConfig, "unilyze", "config.json");
    }

    internal static string GetProjectConfigPath(string projectRoot)
        => Path.Combine(projectRoot, ".unilyze.json");
}
