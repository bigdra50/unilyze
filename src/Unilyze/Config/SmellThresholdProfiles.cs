using Unilyze.Detectors;
using Unilyze.Pipeline;
using System.Text;

namespace Unilyze.Config;

/// <summary>
/// Built-in smell-threshold profiles keyed by <see cref="TypeRole"/>.
/// Provisional unity values are literature-derived (SATT SCAM 2016; Alves ICSM 2010);
/// final per-role calibration is expected from <c>unilyze calibrate</c>.
/// </summary>
internal static class SmellThresholdProfiles
{
    public const string DefaultProfileName = "default";
    public const string UnityProfileName = "unity";

    static readonly IReadOnlyDictionary<TypeRole, EffectiveSmellThresholds> UnityRoleThresholds =
        BuildUnityRoleThresholds();

    static readonly IReadOnlySet<CodeSmellKind> UnityInformationalSmells =
        new HashSet<CodeSmellKind> { CodeSmellKind.LowCohesion };

    public static bool IsKnownProfile(string? profile)
        => profile is null
           or DefaultProfileName
           or UnityProfileName;

    public static string NormalizeProfile(string? profile)
        => string.IsNullOrWhiteSpace(profile) || profile == DefaultProfileName
            ? DefaultProfileName
            : profile;

    public static IReadOnlySet<CodeSmellKind> GetInformationalSmellKinds(string profile)
        => profile == UnityProfileName
            ? UnityInformationalSmells
            : new HashSet<CodeSmellKind>();

    public static EffectiveSmellThresholds ResolveBaseThresholds(string profile, TypeRole role)
    {
        profile = NormalizeProfile(profile);
        if (profile != UnityProfileName)
            return EffectiveSmellThresholds.Default;

        return UnityRoleThresholds.TryGetValue(role, out var thresholds)
            ? thresholds
            : EffectiveSmellThresholds.Default;
    }

    public static EffectiveSmellThresholds ResolveEffectiveThresholds(
        string profile,
        TypeRole role,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, System.Text.Json.JsonElement>>? overrides)
        => EffectiveSmellThresholds.FromOverrides(overrides, ResolveBaseThresholds(profile, role));

    public static string FormatMetricsCliHelp(string? profile)
    {
        profile = NormalizeProfile(profile);
        if (profile != UnityProfileName)
            return SmellThresholds.FormatMetricsCliHelp();

        var sb = new StringBuilder();
        sb.AppendLine($"  Active profile: {UnityProfileName}");
        sb.AppendLine("  Per-role code-smell thresholds (provisional; user smells overrides take precedence):");
        foreach (var role in Enum.GetValues<TypeRole>())
        {
            var thresholds = ResolveEffectiveThresholds(UnityProfileName, role, overrides: null);
            sb.AppendLine($"    [{role}]");
            sb.Append(SmellThresholds.FormatMetricsCliHelp(thresholds).ReplaceLineEndings("\n").TrimEnd());
            sb.AppendLine();
        }

        sb.AppendLine("    Informational (unity profile, not counted as warnings):");
        sb.AppendLine("      LowCohesion          LCOM >= role threshold (Palomba ICSME 2014; recorded as informationalCount)");
        return sb.ToString().TrimEnd();
    }

    static IReadOnlyDictionary<TypeRole, EffectiveSmellThresholds> BuildUnityRoleThresholds()
    {
        var d = EffectiveSmellThresholds.Default;
        return new Dictionary<TypeRole, EffectiveSmellThresholds>
        {
            [TypeRole.PlainCSharp] = d,
            [TypeRole.MonoBehaviour] = d with
            {
                GodClassLinesWarning = 800,
                GodClassMethodsWarning = 30,
                GodClassLinesCritical = 1500,
            },
            [TypeRole.ScriptableObject] = d with
            {
                GodClassLinesWarning = 650,
                GodClassMethodsWarning = 25,
            },
            [TypeRole.EditorExtension] = d with
            {
                GodClassLinesWarning = 700,
                GodClassMethodsWarning = 25,
            },
        };
    }
}
