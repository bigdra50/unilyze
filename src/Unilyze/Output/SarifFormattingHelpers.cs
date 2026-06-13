using Unilyze.Findings;
using Unilyze.Config;
using Unilyze.Detectors;
using Unilyze.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Unilyze.Output;

static class SarifFormattingHelpers
{
    public static JsonObject BuildRuleObject(
        string ruleId,
        CodeSmellKind kind,
        string shortDescription,
        EffectiveSmellThresholds thresholds)
    {
        var ruleObj = new JsonObject
        {
            ["id"] = ruleId,
            ["shortDescription"] = new JsonObject { ["text"] = shortDescription },
            ["defaultConfiguration"] = new JsonObject { ["level"] = "warning" },
            ["helpUri"] = SmellThresholds.GetSarifHelpUri(ruleId),
        };

        var fullDescription = SmellThresholds.GetSarifFullDescription(kind, thresholds);
        if (fullDescription is not null)
            ruleObj["fullDescription"] = new JsonObject { ["text"] = fullDescription };

        var helpText = SmellThresholds.GetSarifHelpText(kind, thresholds);
        var helpMarkdown = SmellThresholds.GetSarifHelpMarkdown(kind, thresholds);
        if (helpText is not null && helpMarkdown is not null)
        {
            ruleObj["help"] = new JsonObject
            {
                ["text"] = helpText,
                ["markdown"] = helpMarkdown,
            };
        }

        var tags = SmellThresholds.GetSarifTags(kind);
        ruleObj["properties"] = new JsonObject
        {
            ["tags"] = new JsonArray(tags.Select(static t => JsonValue.Create(t)).ToArray()),
        };

        return ruleObj;
    }

    public static JsonObject BuildResultObject(
        string ruleId,
        int ruleIndex,
        CodeSmell smell,
        TypeMetrics typeMetrics,
        string projectPath,
        int? occurrenceIndex = null)
    {
        var level = smell.Severity == SmellSeverity.Critical ? "error" : "warning";
        var messageText = smell.MethodName is not null
            ? $"{smell.TypeName}.{smell.MethodName}: {smell.Message}"
            : $"{smell.TypeName}: {smell.Message}";

        var relativePath = string.IsNullOrEmpty(typeMetrics.FilePath)
            ? ""
            : GetRelativePath(projectPath, typeMetrics.FilePath);

        var fingerprint = smell.Id ?? ComputeFingerprint(
            ruleId, relativePath, smell.TypeName, smell.MethodName, occurrenceIndex ?? 0);

        var resultObj = new JsonObject
        {
            ["ruleId"] = ruleId,
            ["ruleIndex"] = ruleIndex,
            ["level"] = level,
            ["message"] = new JsonObject { ["text"] = messageText },
            ["partialFingerprints"] = new JsonObject
            {
                [SarifFormatter.FingerprintKey] = fingerprint,
            },
        };

        var location = BuildLocation(typeMetrics, smell, projectPath);
        if (location is not null)
            resultObj["locations"] = new JsonArray { location };

        resultObj["properties"] = BuildProperties(typeMetrics, smell);
        AddSuppressions(resultObj, smell);
        return resultObj;
    }

    static void AddSuppressions(JsonObject resultObj, CodeSmell smell)
    {
        var suppressions = new JsonArray();

        if (smell.Suppressed == true)
        {
            suppressions.Add(new JsonObject
            {
                ["kind"] = "inSource",
                ["justification"] = smell.SuppressionJustification
                    ?? "Suppressed via unilyze-disable comment",
            });
        }

        if (smell.Baselined == true)
        {
            suppressions.Add(new JsonObject
            {
                ["kind"] = "external",
                ["justification"] = "Baselined in .unilyze/baseline.json",
            });
        }

        if (TriageVerdicts.ExcludesFromGates(smell.Triage))
        {
            suppressions.Add(new JsonObject
            {
                ["kind"] = "external",
                ["justification"] = $"Triage verdict: {smell.Triage}",
            });
        }

        if (suppressions.Count > 0)
            resultObj["suppressions"] = suppressions;
    }

    public static string ComputeFingerprint(
        string ruleId,
        string relativePath,
        string typeName,
        string? methodName,
        int occurrenceIndex)
    {
        var payload = string.Join('\0', ruleId, relativePath, typeName, methodName ?? "", occurrenceIndex.ToString());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
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

        var region = BuildRegion(typeMetrics, smell);
        if (region is not null)
            physicalLocation["region"] = region;

        return new JsonObject { ["physicalLocation"] = physicalLocation };
    }

    static JsonObject? BuildRegion(TypeMetrics typeMetrics, CodeSmell smell)
    {
        if (smell.Line is > 0)
        {
            return new JsonObject
            {
                ["startLine"] = smell.Line.Value,
                ["endLine"] = smell.Line.Value,
            };
        }

        MethodMetrics? method = null;
        if (smell.MethodName is not null)
            method = FindMethod(typeMetrics, smell.MethodName);

        int? startLine = method?.StartLine ?? typeMetrics.StartLine;
        if (startLine is not > 0) return null;

        int? endLine = method is not null
            ? method.LineCount > 0 ? method.StartLine + method.LineCount - 1 : null
            : typeMetrics.LineCount > 0 ? typeMetrics.StartLine + typeMetrics.LineCount - 1 : null;

        var region = new JsonObject { ["startLine"] = startLine.Value };
        if (endLine is > 0 && endLine >= startLine)
            region["endLine"] = endLine.Value;

        return region;
    }

    static JsonObject BuildProperties(TypeMetrics typeMetrics, CodeSmell smell)
    {
        var props = new JsonObject
        {
            ["typeName"] = smell.TypeName,
            ["codeHealth"] = typeMetrics.CodeHealth,
        };

        if (smell.MethodName is null)
        {
            props["lineCount"] = typeMetrics.LineCount;
            props["methodCount"] = typeMetrics.MethodCount;
            if (typeMetrics.Lcom is not null)
                props["lcom"] = typeMetrics.Lcom.Value;
            return props;
        }

        props["methodName"] = smell.MethodName;
        var method = FindMethod(typeMetrics, smell.MethodName);
        if (method is null) return props;

        props["cognitiveComplexity"] = method.CognitiveComplexity;
        props["cyclomaticComplexity"] = method.CyclomaticComplexity;
        props["maxNestingDepth"] = method.MaxNestingDepth;
        props["parameterCount"] = method.ParameterCount;
        props["methodLineCount"] = method.LineCount;
        return props;
    }

    static MethodMetrics? FindMethod(TypeMetrics typeMetrics, string methodName)
    {
        foreach (var method in typeMetrics.Methods)
        {
            if (method.MethodName == methodName)
                return method;
        }

        return null;
    }

    static string GetRelativePath(string projectPath, string filePath)
    {
        if (string.IsNullOrEmpty(projectPath)) return filePath;
        var relative = Path.GetRelativePath(projectPath, filePath);
        return relative.Replace('\\', '/');
    }

    internal readonly record struct OccurrenceKey(string File, string TypeName, string? MethodName, string RuleId);
}
