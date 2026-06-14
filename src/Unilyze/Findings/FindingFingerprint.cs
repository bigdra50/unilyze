using Unilyze.Output;
using Unilyze.Detectors;
using Unilyze.Metrics;
using Unilyze.Pipeline;
namespace Unilyze.Findings;

internal static class FindingFingerprint
{
    internal readonly record struct OccurrenceKey(string File, string TypeName, string? MethodName, string RuleId);
    internal readonly record struct OccurrenceKeyV2(string MemberId, string RuleId);

    public static AnalysisResult AssignIds(AnalysisResult result)
    {
        if (result.TypeMetrics is not { Count: > 0 })
            return result;

        var v1Counts = new Dictionary<OccurrenceKey, int>();
        var v2Counts = new Dictionary<OccurrenceKeyV2, int>();

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

                string id;
                string? legacyId = null;
                if (smell.MemberId is not null)
                {
                    var v2Key = new OccurrenceKeyV2(smell.MemberId, ruleId);
                    v2Counts.TryGetValue(v2Key, out var v2Index);
                    v2Counts[v2Key] = v2Index + 1;
                    id = SarifFormattingHelpers.ComputeFingerprint(
                        ruleId, smell.MemberId, v2Index);

                    var v1Key = new OccurrenceKey(relativePath, smell.TypeName, smell.MethodName, ruleId);
                    v1Counts.TryGetValue(v1Key, out var v1Index);
                    v1Counts[v1Key] = v1Index + 1;
                    legacyId = SarifFormattingHelpers.ComputeFingerprint(
                        ruleId, relativePath, smell.TypeName, smell.MethodName, v1Index);
                }
                else
                {
                    var v1Key = new OccurrenceKey(relativePath, smell.TypeName, smell.MethodName, ruleId);
                    v1Counts.TryGetValue(v1Key, out var v1Index);
                    v1Counts[v1Key] = v1Index + 1;
                    id = SarifFormattingHelpers.ComputeFingerprint(
                        ruleId, relativePath, smell.TypeName, smell.MethodName, v1Index);
                }

                updatedSmells.Add(smell with { Id = id, LegacyId = legacyId });
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
