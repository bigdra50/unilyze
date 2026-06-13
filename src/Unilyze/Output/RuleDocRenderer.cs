using Unilyze.Config;
using Unilyze.Detectors;
using System.Text;

namespace Unilyze.Output;

internal static class RuleDocRenderer
{
    public static string Render(string ruleId)
    {
        var definition = GetDefinition(ruleId);
        var thresholds = EffectiveSmellThresholds.Default;
        var fullDescription = SmellThresholds.GetSarifFullDescription(definition.Kind, thresholds)
            ?? throw new InvalidOperationException($"Missing SARIF full description for {definition.RuleId}.");
        var helpMarkdown = SmellThresholds.GetSarifHelpMarkdown(definition.Kind, thresholds)
            ?? throw new InvalidOperationException($"Missing SARIF help markdown for {definition.RuleId}.");
        var helpText = SmellThresholds.GetSarifHelpText(definition.Kind, thresholds)
            ?? throw new InvalidOperationException($"Missing SARIF help text for {definition.RuleId}.");
        var tags = SmellThresholds.GetSarifTags(definition.Kind);

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"# {definition.RuleId}: {definition.ShortDescription}");
        sb.AppendLine();
        sb.AppendLine("## What and why");
        sb.AppendLine();
        sb.AppendLine(fullDescription);
        sb.AppendLine();
        sb.AppendLine(helpMarkdown);

        var thresholdRows = GetDefaultThresholdRows(definition.Kind, thresholds);
        if (thresholdRows.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Default thresholds");
            sb.AppendLine();
            sb.AppendLine("| Severity | Condition |");
            sb.AppendLine("|----------|-----------|");
            foreach (var (severity, condition) in thresholdRows)
                sb.AppendLine($"| {severity} | {condition} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Fix guidance");
        sb.AppendLine();
        sb.AppendLine(helpText);
        sb.AppendLine();
        sb.AppendLine("## Tags");
        sb.AppendLine();
        sb.AppendLine(string.Join(", ", tags.Select(static tag => $"`{tag}`")));
        sb.AppendLine();
        sb.AppendLine("## Suppression");
        sb.AppendLine();
        AppendSuppression(sb, definition.RuleId);

        return sb.ToString().ReplaceLineEndings("\n");
    }

    public static string GetSeverityEntryPoints(string ruleId)
    {
        var definition = GetDefinition(ruleId);
        var thresholdRows = GetDefaultThresholdRows(definition.Kind, EffectiveSmellThresholds.Default);
        if (thresholdRows.Count > 0)
            return string.Join("; ", thresholdRows.Select(static row => $"{row.Severity}: {row.Condition}"));

        return definition.Kind is CodeSmellKind.BoxingAllocation
            or CodeSmellKind.ClosureCapture
            or CodeSmellKind.ParamsArrayAllocation
                ? "Warning; Critical in Unity hot paths"
                : "Warning";
    }

    static (string RuleId, CodeSmellKind Kind, string ShortDescription) GetDefinition(string ruleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);

        foreach (var definition in SarifFormatter.EnumerateRuleDefinitions())
        {
            if (string.Equals(definition.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))
                return definition;
        }

        throw new ArgumentOutOfRangeException(nameof(ruleId), ruleId, "Unknown SARIF rule id.");
    }

    static IReadOnlyList<(string Severity, string Condition)> GetDefaultThresholdRows(
        CodeSmellKind kind,
        EffectiveSmellThresholds thresholds) => kind switch
    {
        CodeSmellKind.GodClass =>
        [
            ("Warning", $"lines >= {thresholds.GodClassLinesWarning} or methods >= {thresholds.GodClassMethodsWarning}"),
            ("Critical", $"lines >= {thresholds.GodClassLinesCritical}"),
        ],
        CodeSmellKind.LongMethod =>
        [
            ("Warning", $"lines >= {thresholds.LongMethodLinesWarning} or CogCC >= {thresholds.LongMethodCogCcWarning}"),
            ("Critical", $"lines >= {thresholds.LongMethodLinesCritical} or CogCC >= {thresholds.LongMethodCogCcCritical}"),
        ],
        CodeSmellKind.ExcessiveParameters =>
        [
            ("Warning", $"parameter count > {thresholds.ExcessiveParametersMax}"),
        ],
        CodeSmellKind.HighComplexity =>
        [
            ("Warning", $"CycCC >= {thresholds.HighComplexityCycCcWarning} or CogCC >= {thresholds.HighComplexityCogCcWarning}"),
        ],
        CodeSmellKind.DeepNesting =>
        [
            ("Warning", $"nesting depth >= {thresholds.DeepNestingDepthWarning}"),
            ("Critical", $"nesting depth >= {thresholds.DeepNestingDepthCritical}"),
        ],
        CodeSmellKind.LowCohesion =>
        [
            ("Warning", $"LCOM >= {thresholds.LowCohesionLcomWarning:0.0}"),
        ],
        CodeSmellKind.HighCoupling =>
        [
            ("Warning", $"CBO >= {thresholds.HighCouplingCboWarning}"),
            ("Critical", $"CBO >= {thresholds.HighCouplingCboCritical}"),
        ],
        CodeSmellKind.LowMaintainability =>
        [
            ("Warning", $"MI < {thresholds.LowMaintainabilityMiWarning:0}"),
        ],
        CodeSmellKind.DeepInheritance =>
        [
            ("Warning", $"DIT >= {thresholds.DeepInheritanceDitWarning}"),
        ],
        _ => [],
    };

    static void AppendSuppression(StringBuilder sb, string ruleId)
    {
        var ruleNumber = int.Parse(ruleId.AsSpan(3));
        if (ruleNumber == 9)
        {
            sb.AppendLine($"Inline suppression is not supported for `{ruleId}` because dependency cycles have no single source location.");
            sb.AppendLine($"Disable it project-wide with `\"rules\": {{ \"{ruleId}\": \"off\" }}` in `.unilyze.json`.");
            return;
        }

        if (ruleNumber is >= 11 and <= 25)
        {
            sb.AppendLine($"Use `// unilyze-disable-next-line {ruleId} -- reason` immediately before the reported line.");
            sb.AppendLine("These detector findings are line-based, so separate overload occurrences can be suppressed independently.");
            sb.AppendLine($"For project-wide suppression, use `\"rules\": {{ \"{ruleId}\": \"off\" }}`; use a baseline to freeze existing findings while reporting new ones.");
            return;
        }

        sb.AppendLine($"Place `// unilyze-disable {ruleId} -- reason` in the leading trivia of the reported type or method declaration.");
        sb.AppendLine("Metric findings are matched by type or method name, so suppressing one overload also suppresses same-name overloads.");
        sb.AppendLine("For partial types, place the directive on the declaration indexed by unilyze.");
        sb.AppendLine($"For project-wide suppression, use `\"rules\": {{ \"{ruleId}\": \"off\" }}`; use a baseline to freeze existing findings while reporting new ones.");
    }
}
