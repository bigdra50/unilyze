using System.Text.Json;
using System.Text.Json.Nodes;

namespace Unilyze;

public static class SarifFormatter
{
    const string SchemaUri = "https://schemastore.azurewebsites.net/schemas/json/sarif-2.1.0-rtm.5.json";
    const string ToolName = "unilyze";
    const string InformationUri = "https://github.com/bigdra50/unilyze";

    public const string FingerprintKey = "unilyzeFingerprint/v1";

    static readonly (string RuleId, CodeSmellKind Kind, string ShortDescription)[] RuleDefinitions =
    [
        ("UNI001", CodeSmellKind.GodClass, "God class detected"),
        ("UNI002", CodeSmellKind.LongMethod, "Long method detected"),
        ("UNI003", CodeSmellKind.ExcessiveParameters, "Excessive parameters"),
        ("UNI004", CodeSmellKind.HighComplexity, "High complexity"),
        ("UNI005", CodeSmellKind.DeepNesting, "Deep nesting"),
        ("UNI006", CodeSmellKind.LowCohesion, "Low cohesion"),
        ("UNI007", CodeSmellKind.HighCoupling, "High coupling"),
        ("UNI008", CodeSmellKind.LowMaintainability, "Low maintainability"),
        ("UNI009", CodeSmellKind.CyclicDependency, "Cyclic dependency"),
        ("UNI010", CodeSmellKind.DeepInheritance, "Deep inheritance hierarchy"),
        ("UNI011", CodeSmellKind.BoxingAllocation, "Boxing allocation detected"),
        ("UNI012", CodeSmellKind.ClosureCapture, "Closure variable capture detected"),
        ("UNI013", CodeSmellKind.ParamsArrayAllocation, "Implicit params array allocation"),
        ("UNI014", CodeSmellKind.CatchAllException, "Catch-all exception without rethrow"),
        ("UNI015", CodeSmellKind.MissingInnerException, "Missing inner exception in rethrow"),
        ("UNI016", CodeSmellKind.ThrowingSystemException, "Throwing System.Exception directly"),
        ("UNI022", CodeSmellKind.AsyncVoidMethod, "async void method"),
        ("UNI023", CodeSmellKind.BlockingTaskWait, "Blocking wait on Task"),
        ("UNI017", CodeSmellKind.ExpensiveUnityApiInHotPath, "Expensive Unity API in hot path"),
        ("UNI018", CodeSmellKind.LinqInHotPath, "LINQ in hot path"),
        ("UNI019", CodeSmellKind.CollectionAllocationInHotPath, "Collection allocation in hot path"),
        ("UNI020", CodeSmellKind.StringConcatenationInHotPath, "String concatenation in hot path"),
        ("UNI021", CodeSmellKind.WeakTemporization, "Frame-rate dependent update"),
    ];

    public static string Generate(AnalysisResult result, EffectiveSmellThresholds? thresholds = null)
    {
        thresholds ??= EffectiveSmellThresholds.Default;
        var version = typeof(SarifFormatter).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

        var sarif = new JsonObject
        {
            ["$schema"] = SchemaUri,
            ["version"] = "2.1.0",
            ["runs"] = new JsonArray
            {
                BuildRun(result, version, thresholds)
            }
        };

        return sarif.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    static readonly Dictionary<string, CodeSmellKind> RuleIdToKind = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UNI001"] = CodeSmellKind.GodClass,
        ["UNI002"] = CodeSmellKind.LongMethod,
        ["UNI003"] = CodeSmellKind.ExcessiveParameters,
        ["UNI004"] = CodeSmellKind.HighComplexity,
        ["UNI005"] = CodeSmellKind.DeepNesting,
        ["UNI006"] = CodeSmellKind.LowCohesion,
        ["UNI007"] = CodeSmellKind.HighCoupling,
        ["UNI008"] = CodeSmellKind.LowMaintainability,
        ["UNI009"] = CodeSmellKind.CyclicDependency,
        ["UNI010"] = CodeSmellKind.DeepInheritance,
        ["UNI011"] = CodeSmellKind.BoxingAllocation,
        ["UNI012"] = CodeSmellKind.ClosureCapture,
        ["UNI013"] = CodeSmellKind.ParamsArrayAllocation,
        ["UNI014"] = CodeSmellKind.CatchAllException,
        ["UNI015"] = CodeSmellKind.MissingInnerException,
        ["UNI016"] = CodeSmellKind.ThrowingSystemException,
        ["UNI017"] = CodeSmellKind.ExpensiveUnityApiInHotPath,
        ["UNI018"] = CodeSmellKind.LinqInHotPath,
        ["UNI019"] = CodeSmellKind.CollectionAllocationInHotPath,
        ["UNI020"] = CodeSmellKind.StringConcatenationInHotPath,
        ["UNI021"] = CodeSmellKind.WeakTemporization,
        ["UNI022"] = CodeSmellKind.AsyncVoidMethod,
        ["UNI023"] = CodeSmellKind.BlockingTaskWait,
    };

    public static bool TryGetKind(string ruleId, out CodeSmellKind kind)
        => RuleIdToKind.TryGetValue(ruleId, out kind);

    public static string? GetRuleId(CodeSmellKind kind)
    {
        foreach (var (ruleId, ruleKind, _) in RuleDefinitions)
        {
            if (ruleKind == kind)
                return ruleId;
        }

        return null;
    }

    public static IEnumerable<(string RuleId, CodeSmellKind Kind)> EnumerateRules()
        => RuleDefinitions.Select(static r => (r.RuleId, r.Kind));

    internal static string ComputeFingerprint(
        string ruleId,
        string relativePath,
        string typeName,
        string? methodName,
        int occurrenceIndex)
        => SarifFormattingHelpers.ComputeFingerprint(ruleId, relativePath, typeName, methodName, occurrenceIndex);

    static JsonObject BuildRun(AnalysisResult result, string version, EffectiveSmellThresholds thresholds)
    {
        var ruleIndexByKind = new Dictionary<CodeSmellKind, int>();
        var rulesArray = new JsonArray();
        for (var i = 0; i < RuleDefinitions.Length; i++)
        {
            var (ruleId, kind, desc) = RuleDefinitions[i];
            ruleIndexByKind[kind] = i;
            rulesArray.Add(SarifFormattingHelpers.BuildRuleObject(ruleId, kind, desc, thresholds));
        }

        var run = new JsonObject
        {
            ["tool"] = new JsonObject
            {
                ["driver"] = new JsonObject
                {
                    ["name"] = ToolName,
                    ["version"] = version,
                    ["informationUri"] = InformationUri,
                    ["rules"] = rulesArray,
                }
            },
            ["results"] = BuildResults(result, ruleIndexByKind),
        };

        if (!string.IsNullOrEmpty(result.ProjectPath))
        {
            var projectUri = new Uri(Path.GetFullPath(result.ProjectPath) + Path.DirectorySeparatorChar).ToString();
            run["originalUriBaseIds"] = new JsonObject
            {
                ["%SRCROOT%"] = new JsonObject { ["uri"] = projectUri }
            };
        }

        return run;
    }

    static JsonArray BuildResults(AnalysisResult result, Dictionary<CodeSmellKind, int> ruleIndexByKind)
    {
        var results = new JsonArray();
        if (result.TypeMetrics is null) return results;

        foreach (var typeMetrics in result.TypeMetrics)
        {
            if (typeMetrics.CodeSmells is null) continue;

            foreach (var smell in typeMetrics.CodeSmells)
            {
                if (!ruleIndexByKind.TryGetValue(smell.Kind, out var ruleIndex)) continue;

                var ruleId = RuleDefinitions[ruleIndex].RuleId;
                results.Add(SarifFormattingHelpers.BuildResultObject(
                    ruleId, ruleIndex, smell, typeMetrics, result.ProjectPath));
            }
        }

        return results;
    }
}
