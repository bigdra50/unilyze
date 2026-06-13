using Unilyze.Output;
using Unilyze.Detectors;
using Unilyze.Metrics;
using Unilyze.Pipeline;
namespace Unilyze.Findings;

internal static class FindingFingerprint
{
    internal readonly record struct OccurrenceKey(string File, string TypeName, string? MethodName, string RuleId);

    public static AnalysisResult AssignIds(AnalysisResult result)
    {
        if (result.TypeMetrics is not { Count: > 0 })
            return result;

        var occurrenceCounts = new Dictionary<OccurrenceKey, int>();
        var updatedMetrics = result.TypeMetrics.Select(typeMetrics =>
        {
            if (typeMetrics.CodeSmells is not { Count: > 0 })
                return typeMetrics;

            var relativePath = GetRelativePath(result.ProjectPath, typeMetrics.FilePath);
            var updatedSmells = new List<CodeSmell>(typeMetrics.CodeSmells.Count);

            foreach (var smell in typeMetrics.CodeSmells)
            {
                var ruleId = SarifFormatter.GetRuleId(smell.Kind);
                if (ruleId is null)
                {
                    updatedSmells.Add(smell);
                    continue;
                }

                var key = new OccurrenceKey(relativePath, smell.TypeName, smell.MethodName, ruleId);
                occurrenceCounts.TryGetValue(key, out var occurrenceIndex);
                occurrenceCounts[key] = occurrenceIndex + 1;

                var id = SarifFormattingHelpers.ComputeFingerprint(
                    ruleId, relativePath, smell.TypeName, smell.MethodName, occurrenceIndex);
                updatedSmells.Add(smell with { Id = id });
            }

            return typeMetrics with { CodeSmells = updatedSmells };
        }).ToList();

        return result with { TypeMetrics = updatedMetrics };
    }

    static string GetRelativePath(string projectPath, string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return "";
        if (string.IsNullOrEmpty(projectPath))
            return filePath.Replace('\\', '/');
        return Path.GetRelativePath(projectPath, filePath).Replace('\\', '/');
    }
}
