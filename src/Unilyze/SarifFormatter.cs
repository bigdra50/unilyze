using System.Text.Json;
using System.Text.Json.Nodes;

namespace Unilyze;

public static class SarifFormatter
{
    const string SchemaUri = "https://schemastore.azurewebsites.net/schemas/json/sarif-2.1.0-rtm.5.json";
    const string ToolName = "unilyze";
    const string InformationUri = "https://github.com/bigdra50/unilyze";

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
    ];

    public static string Generate(AnalysisResult result)
    {
        var version = typeof(SarifFormatter).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

        var sarif = new JsonObject
        {
            ["$schema"] = SchemaUri,
            ["version"] = "2.1.0",
            ["runs"] = new JsonArray
            {
                BuildRun(result, version)
            }
        };

        return sarif.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    static JsonObject BuildRun(AnalysisResult result, string version)
    {
        var ruleIndexByKind = new Dictionary<CodeSmellKind, int>();
        var rulesArray = new JsonArray();
        for (var i = 0; i < RuleDefinitions.Length; i++)
        {
            var (ruleId, kind, desc) = RuleDefinitions[i];
            ruleIndexByKind[kind] = i;
            rulesArray.Add(new JsonObject
            {
                ["id"] = ruleId,
                ["shortDescription"] = new JsonObject { ["text"] = desc },
                ["defaultConfiguration"] = new JsonObject { ["level"] = "warning" },
            });
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
                var level = smell.Severity == SmellSeverity.Critical ? "error" : "warning";

                var messageText = smell.MethodName is not null
                    ? $"{smell.TypeName}.{smell.MethodName}: {smell.Message}"
                    : $"{smell.TypeName}: {smell.Message}";

                var resultObj = new JsonObject
                {
                    ["ruleId"] = ruleId,
                    ["ruleIndex"] = ruleIndex,
                    ["level"] = level,
                    ["message"] = new JsonObject { ["text"] = messageText },
                };

                var location = BuildLocation(typeMetrics, smell, result.ProjectPath);
                if (location is not null)
                {
                    resultObj["locations"] = new JsonArray { location };
                }

                var properties = BuildProperties(typeMetrics, smell);
                if (properties is not null)
                {
                    resultObj["properties"] = properties;
                }

                results.Add(resultObj);
            }
        }

        return results;
    }

    static JsonObject? BuildLocation(TypeMetrics typeMetrics, CodeSmell smell, string projectPath)
    {
        if (string.IsNullOrEmpty(typeMetrics.FilePath)) return null;

        var relativePath = GetRelativePath(projectPath, typeMetrics.FilePath);

        var physicalLocation = new JsonObject
        {
            ["artifactLocation"] = new JsonObject
            {
                ["uri"] = relativePath,
                ["uriBaseId"] = "%SRCROOT%",
            }
        };

        int? startLine = null;
        if (smell.MethodName is not null)
        {
            var method = typeMetrics.Methods.FirstOrDefault(m => m.MethodName == smell.MethodName);
            startLine = method?.StartLine;
        }
        startLine ??= typeMetrics.StartLine;

        if (startLine is > 0)
        {
            physicalLocation["region"] = new JsonObject
            {
                ["startLine"] = startLine.Value,
            };
        }

        return new JsonObject { ["physicalLocation"] = physicalLocation };
    }

    static JsonObject? BuildProperties(TypeMetrics typeMetrics, CodeSmell smell)
    {
        var props = new JsonObject
        {
            ["typeName"] = smell.TypeName,
            ["codeHealth"] = typeMetrics.CodeHealth,
        };

        if (smell.MethodName is not null)
        {
            props["methodName"] = smell.MethodName;
            var method = typeMetrics.Methods.FirstOrDefault(m => m.MethodName == smell.MethodName);
            if (method is not null)
            {
                props["cognitiveComplexity"] = method.CognitiveComplexity;
                props["cyclomaticComplexity"] = method.CyclomaticComplexity;
                props["maxNestingDepth"] = method.MaxNestingDepth;
                props["parameterCount"] = method.ParameterCount;
                props["methodLineCount"] = method.LineCount;
            }
        }
        else
        {
            props["lineCount"] = typeMetrics.LineCount;
            props["methodCount"] = typeMetrics.MethodCount;
            if (typeMetrics.Lcom is not null)
                props["lcom"] = typeMetrics.Lcom.Value;
        }

        return props;
    }

    static string GetRelativePath(string projectPath, string filePath)
    {
        if (string.IsNullOrEmpty(projectPath)) return filePath;
        var relative = Path.GetRelativePath(projectPath, filePath);
        return relative.Replace('\\', '/');
    }
}
