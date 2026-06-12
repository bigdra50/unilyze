using System.Text;

namespace Unilyze;

internal static class MarkdownDeltaScoreFormatter
{
    internal static void Append(StringBuilder sb, DiffResult diff)
    {
        sb.AppendLine("### Delta risk");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| deltaScore | {MarkdownDiffFormatter.Fmt(diff.DeltaScore)} |");
        sb.AppendLine($"| Low-risk changes | {diff.LowRiskChangeCount} |");
        sb.AppendLine($"| High-risk changes | {diff.HighRiskChangeCount} |");
    }
}
