using System.Text.Json;

namespace Unilyze;

/// <summary>
/// Effective code-smell detection thresholds for a single analysis run.
/// Defaults come from <see cref="SmellThresholds"/>; per-smell overrides from config are applied on top.
/// </summary>
public sealed record EffectiveSmellThresholds(
    int GodClassLinesWarning,
    int GodClassMethodsWarning,
    int GodClassLinesCritical,
    int LongMethodLinesWarning,
    int LongMethodCogCcWarning,
    int LongMethodLinesCritical,
    int LongMethodCogCcCritical,
    int ExcessiveParametersMax,
    int HighComplexityCycCcWarning,
    int HighComplexityCogCcWarning,
    int DeepNestingDepthWarning,
    int DeepNestingDepthCritical,
    double LowCohesionLcomWarning,
    int HighCouplingCboWarning,
    int HighCouplingCboCritical,
    double LowMaintainabilityMiWarning,
    int DeepInheritanceDitWarning)
{
    public static EffectiveSmellThresholds Default { get; } = new(
        SmellThresholds.GodClassLinesWarning,
        SmellThresholds.GodClassMethodsWarning,
        SmellThresholds.GodClassLinesCritical,
        SmellThresholds.LongMethodLinesWarning,
        SmellThresholds.LongMethodCogCcWarning,
        SmellThresholds.LongMethodLinesCritical,
        SmellThresholds.LongMethodCogCcCritical,
        SmellThresholds.ExcessiveParametersMax,
        SmellThresholds.HighComplexityCycCcWarning,
        SmellThresholds.HighComplexityCogCcWarning,
        SmellThresholds.DeepNestingDepthWarning,
        SmellThresholds.DeepNestingDepthCritical,
        SmellThresholds.LowCohesionLcomWarning,
        SmellThresholds.HighCouplingCboWarning,
        SmellThresholds.HighCouplingCboCritical,
        SmellThresholds.LowMaintainabilityMiWarning,
        SmellThresholds.DeepInheritanceDitWarning);

    public static EffectiveSmellThresholds FromOverrides(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? overrides)
    {
        if (overrides is not { Count: > 0 })
            return Default;

        var result = Default;
        foreach (var (smellName, thresholdMap) in overrides)
        {
            if (!SmellKeyMap.TryGetValue(smellName, out var keyMap))
            {
                Console.Error.WriteLine(
                    $"Warning: Unknown smell name '{smellName}' in config; ignoring.");
                continue;
            }

            foreach (var (thresholdKey, value) in thresholdMap)
            {
                if (!keyMap.TryGetValue(thresholdKey, out var apply))
                {
                    Console.Error.WriteLine(
                        $"Warning: Unknown threshold key '{thresholdKey}' for smell '{smellName}'; ignoring.");
                    continue;
                }

                if (!TryParseNumeric(value, out var numeric))
                {
                    Console.Error.WriteLine(
                        $"Warning: Invalid threshold value for '{smellName}.{thresholdKey}'; ignoring.");
                    continue;
                }

                result = apply(result, numeric);
            }
        }

        return result;
    }

    static readonly Dictionary<string, Dictionary<string, ApplyOverride>> SmellKeyMap =
        BuildSmellKeyMap();

    static Dictionary<string, Dictionary<string, ApplyOverride>> BuildSmellKeyMap()
    {
        var map = new Dictionary<string, Dictionary<string, ApplyOverride>>(StringComparer.OrdinalIgnoreCase);

        AddSmell(map, "GodClass", [
            ("lines", (t, v) => t with { GodClassLinesWarning = ToInt(v) }),
            ("methods", (t, v) => t with { GodClassMethodsWarning = ToInt(v) }),
            ("criticalLines", (t, v) => t with { GodClassLinesCritical = ToInt(v) }),
        ]);

        AddSmell(map, "LongMethod", [
            ("lines", (t, v) => t with { LongMethodLinesWarning = ToInt(v) }),
            ("cogCc", (t, v) => t with { LongMethodCogCcWarning = ToInt(v) }),
            ("criticalLines", (t, v) => t with { LongMethodLinesCritical = ToInt(v) }),
            ("criticalCogCc", (t, v) => t with { LongMethodCogCcCritical = ToInt(v) }),
        ]);

        AddSmell(map, "ExcessiveParameters", [
            ("max", (t, v) => t with { ExcessiveParametersMax = ToInt(v) }),
        ]);

        AddSmell(map, "HighComplexity", [
            ("cycCc", (t, v) => t with { HighComplexityCycCcWarning = ToInt(v) }),
            ("cogCc", (t, v) => t with { HighComplexityCogCcWarning = ToInt(v) }),
        ]);

        AddSmell(map, "DeepNesting", [
            ("depth", (t, v) => t with { DeepNestingDepthWarning = ToInt(v) }),
            ("criticalDepth", (t, v) => t with { DeepNestingDepthCritical = ToInt(v) }),
        ]);

        AddSmell(map, "LowCohesion", [
            ("lcom", (t, v) => t with { LowCohesionLcomWarning = ToDouble(v) }),
        ]);

        AddSmell(map, "HighCoupling", [
            ("cbo", (t, v) => t with { HighCouplingCboWarning = ToInt(v) }),
            ("criticalCbo", (t, v) => t with { HighCouplingCboCritical = ToInt(v) }),
        ]);

        AddSmell(map, "LowMaintainability", [
            ("mi", (t, v) => t with { LowMaintainabilityMiWarning = ToDouble(v) }),
        ]);

        AddSmell(map, "DeepInheritance", [
            ("dit", (t, v) => t with { DeepInheritanceDitWarning = ToInt(v) }),
        ]);

        return map;
    }

    static void AddSmell(
        Dictionary<string, Dictionary<string, ApplyOverride>> map,
        string smellName,
        (string Key, ApplyOverride Apply)[] entries)
    {
        var keyMap = new Dictionary<string, ApplyOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, apply) in entries)
            keyMap[key] = apply;
        map[smellName] = keyMap;
    }

    static bool TryParseNumeric(JsonElement value, out double numeric)
    {
        numeric = 0;
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                numeric = value.GetDouble();
                return true;
            case JsonValueKind.String when double.TryParse(value.GetString(), out var parsed):
                numeric = parsed;
                return true;
            default:
                return false;
        }
    }

    static int ToInt(double value) => (int)Math.Round(value);
    static double ToDouble(double value) => value;

    delegate EffectiveSmellThresholds ApplyOverride(EffectiveSmellThresholds thresholds, double value);

    internal static IEnumerable<(string Key, string Value, bool Overridden)> BuildConfigEntries(
        EffectiveSmellThresholds current)
    {
        var defaults = Default;
        yield return Entry("GodClass.lines", current.GodClassLinesWarning, defaults.GodClassLinesWarning);
        yield return Entry("GodClass.methods", current.GodClassMethodsWarning, defaults.GodClassMethodsWarning);
        yield return Entry("GodClass.criticalLines", current.GodClassLinesCritical, defaults.GodClassLinesCritical);
        yield return Entry("LongMethod.lines", current.LongMethodLinesWarning, defaults.LongMethodLinesWarning);
        yield return Entry("LongMethod.cogCc", current.LongMethodCogCcWarning, defaults.LongMethodCogCcWarning);
        yield return Entry("LongMethod.criticalLines", current.LongMethodLinesCritical, defaults.LongMethodLinesCritical);
        yield return Entry("LongMethod.criticalCogCc", current.LongMethodCogCcCritical, defaults.LongMethodCogCcCritical);
        yield return Entry("ExcessiveParameters.max", current.ExcessiveParametersMax, defaults.ExcessiveParametersMax);
        yield return Entry("HighComplexity.cycCc", current.HighComplexityCycCcWarning, defaults.HighComplexityCycCcWarning);
        yield return Entry("HighComplexity.cogCc", current.HighComplexityCogCcWarning, defaults.HighComplexityCogCcWarning);
        yield return Entry("DeepNesting.depth", current.DeepNestingDepthWarning, defaults.DeepNestingDepthWarning);
        yield return Entry("DeepNesting.criticalDepth", current.DeepNestingDepthCritical, defaults.DeepNestingDepthCritical);
        yield return Entry("LowCohesion.lcom", current.LowCohesionLcomWarning, defaults.LowCohesionLcomWarning, "0.0");
        yield return Entry("HighCoupling.cbo", current.HighCouplingCboWarning, defaults.HighCouplingCboWarning);
        yield return Entry("HighCoupling.criticalCbo", current.HighCouplingCboCritical, defaults.HighCouplingCboCritical);
        yield return Entry("LowMaintainability.mi", current.LowMaintainabilityMiWarning, defaults.LowMaintainabilityMiWarning, "0");
        yield return Entry("DeepInheritance.dit", current.DeepInheritanceDitWarning, defaults.DeepInheritanceDitWarning);
    }

    static (string Key, string Value, bool Overridden) Entry(string key, int value, int defaultValue)
        => (key, value.ToString(), value != defaultValue);

    static (string Key, string Value, bool Overridden) Entry(
        string key, double value, double defaultValue, string format)
        => (key, value.ToString(format), value != defaultValue);
}
