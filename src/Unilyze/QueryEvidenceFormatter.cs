using System.Text;
using System.Text.Json;

namespace Unilyze;

internal static class QueryEvidenceFormatter
{
    public static string ToMarkdown(QueryResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Query Evidence Pack");
        sb.AppendLine();
        sb.AppendLine($"Project: `{result.ProjectPath}` | Analyzed: {result.AnalyzedAt:u}");
        sb.AppendLine();

        if (result.Types.Count == 0)
        {
            sb.AppendLine("_No types matched the selection._");
            return sb.ToString().TrimEnd() + Environment.NewLine;
        }

        for (var i = 0; i < result.Types.Count; i++)
        {
            if (i > 0)
                sb.AppendLine();
            AppendTypePack(sb, result.Types[i]);
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string ToJson(QueryResult result)
    {
        var options = new JsonSerializerOptions(AnalysisJsonContext.Default.QueryResult.Options)
        {
            WriteIndented = false
        };
        return JsonSerializer.Serialize(result, options);
    }

    static void AppendTypePack(StringBuilder sb, TypeEvidencePack pack)
    {
        var displayName = string.IsNullOrEmpty(pack.Namespace)
            ? pack.TypeName
            : $"{pack.Namespace}.{pack.TypeName}";
        var anchorSuffix = pack.Anchor is null ? "" : $" @ `{pack.Anchor}`";
        sb.AppendLine($"## {displayName} — CH {pack.Metrics.CodeHealth:F1}{anchorSuffix}");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| --- | --- |");
        AppendMetricRow(sb, "codeHealth", pack.Metrics.CodeHealth.ToString("F1"));
        AppendMetricRow(sb, "cbo", FormatNullable(pack.Metrics.Cbo));
        AppendMetricRow(sb, "lcom", FormatNullable(pack.Metrics.Lcom));
        AppendMetricRow(sb, "dit", FormatNullable(pack.Metrics.Dit));
        AppendMetricRow(sb, "wmc", FormatNullable(pack.Metrics.Wmc));
        AppendMetricRow(sb, "lineCount", pack.Metrics.LineCount.ToString());
        AppendMetricRow(sb, "methodCount", pack.Metrics.MethodCount.ToString());
        AppendMetricRow(sb, "maxCognitiveComplexity", pack.Metrics.MaxCognitiveComplexity.ToString());
        AppendMetricRow(sb, "boxingCount", FormatNullable(pack.Metrics.BoxingCount));
        AppendMetricRow(sb, "closureCaptureCount", FormatNullable(pack.Metrics.ClosureCaptureCount));
        AppendMetricRow(sb, "paramsAllocationCount", FormatNullable(pack.Metrics.ParamsAllocationCount));

        AppendSmells(sb, pack.Smells);
        AppendDependencies(sb, "Inbound", pack.InboundDependencies);
        AppendDependencies(sb, "Outbound", pack.OutboundDependencies);
        AppendTopMethods(sb, pack.TopMethods);
    }

    static void AppendSmells(StringBuilder sb, IReadOnlyList<TypeEvidenceSmell> smells)
    {
        sb.AppendLine();
        sb.AppendLine("### Smells");
        if (smells.Count == 0)
        {
            sb.AppendLine("- _none_");
            return;
        }

        foreach (var smell in smells)
        {
            var anchor = smell.Anchor is null ? "" : $" `{smell.Anchor}`";
            var method = smell.MethodName is null ? "" : $" ({smell.MethodName})";
            sb.AppendLine($"- [{smell.Severity}] {smell.Kind}{method}{anchor}: {smell.Message}");
        }
    }

    static void AppendDependencies(
        StringBuilder sb,
        string direction,
        IReadOnlyList<TypeEvidenceDependencyGroup> groups)
    {
        sb.AppendLine();
        sb.AppendLine($"### Dependencies ({direction})");
        if (groups.Count == 0)
        {
            sb.AppendLine("- _none_");
            return;
        }

        foreach (var group in groups)
        {
            var peers = group.Peers.Count == 0
                ? ""
                : ": " + string.Join(", ", group.Peers);
            sb.AppendLine($"- {group.Kind} ({group.Count}){peers}");
        }
    }

    static void AppendTopMethods(StringBuilder sb, IReadOnlyList<TypeEvidenceMethod> methods)
    {
        sb.AppendLine();
        sb.AppendLine("### Top Methods (by CogCC)");
        if (methods.Count == 0)
        {
            sb.AppendLine("- _none_");
            return;
        }

        foreach (var method in methods)
        {
            var anchor = method.Anchor is null ? "" : $" `{method.Anchor}`";
            sb.AppendLine($"- {method.MethodName} (CogCC {method.CognitiveComplexity}){anchor}");
        }
    }

    static void AppendMetricRow(StringBuilder sb, string name, string value) =>
        sb.AppendLine($"| {name} | {value} |");

    static string FormatNullable(int? value) => value?.ToString() ?? "-";

    static string FormatNullable(double? value) => value?.ToString("F2") ?? "-";
}
