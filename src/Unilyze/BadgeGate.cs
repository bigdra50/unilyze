using System.Globalization;

namespace Unilyze;

internal enum GateOutcome { Pass, Fail, UsageError }

internal sealed record BadgeGateResult(GateOutcome Outcome, string? Message);

/// <summary>
/// Pure evaluation of badge quality gates (--fail-under / --fail-over).
/// Returns a Pass/Fail/UsageError outcome plus a human-readable reason,
/// keeping the exit-code mapping in <see cref="BadgeRunner"/>.
/// </summary>
internal static class BadgeGate
{
    internal static BadgeGateResult ValidateOptions(BadgeMetric metric, string? failUnder, string? failOver)
    {
        if (failUnder is null && failOver is null)
            return Pass();

        return metric switch
        {
            BadgeMetric.CodeHealth or BadgeMetric.Mi => ValidateThresholdMetric(metric, failUnder, failOver),
            BadgeMetric.Smells => ValidateSmellsOptions(failUnder, failOver),
            BadgeMetric.Dup => BadgeDupGate.ValidateOptions(failUnder, failOver),
            _ => UsageError($"Unsupported metric: {metric}")
        };
    }

    internal static BadgeGateResult Evaluate(
        BadgeMetric metric, StatuslineFormatter.Summary summary, string? failUnder, string? failOver)
        => Evaluate(metric, summary, failUnder, failOver, duplicationPercent: null);

    internal static BadgeGateResult Evaluate(
        BadgeMetric metric,
        StatuslineFormatter.Summary summary,
        string? failUnder,
        string? failOver,
        double? duplicationPercent)
    {
        var validation = ValidateOptions(metric, failUnder, failOver);
        if (validation.Outcome != GateOutcome.Pass)
            return validation;

        if (failUnder is null && failOver is null)
            return Pass();

        if (metric == BadgeMetric.Dup)
            return BadgeDupGate.Evaluate(duplicationPercent, failOver);

        var unavailable = EvaluateAvailability(metric, summary);
        if (unavailable is not null)
            return unavailable;

        return metric switch
        {
            BadgeMetric.CodeHealth or BadgeMetric.Mi => EvaluateThreshold(metric, summary, failUnder),
            BadgeMetric.Smells => EvaluateSmells(summary, failOver),
            _ => UsageError($"Unsupported metric: {metric}")
        };
    }

    static BadgeGateResult ValidateThresholdMetric(BadgeMetric metric, string? failUnder, string? failOver)
    {
        if (failOver is not null)
            return UsageError(
                $"--fail-over is only valid with --metric smells or dup. Use --fail-under for metric '{MetricName(metric)}'.");

        if (!TryParseDouble(failUnder, out _))
            return UsageError($"--fail-under requires a numeric value (got '{failUnder}').");

        return Pass();
    }

    static BadgeGateResult ValidateSmellsOptions(string? failUnder, string? failOver)
    {
        if (failUnder is not null)
            return UsageError("--fail-under is not valid with --metric smells. Use --fail-over <count>.");

        if (!TryParseInt(failOver, out var count) || count < 0)
            return UsageError($"--fail-over requires a non-negative integer value (got '{failOver}').");

        return Pass();
    }

    static BadgeGateResult? EvaluateAvailability(BadgeMetric metric, StatuslineFormatter.Summary summary)
    {
        if (summary.TypeCount == 0)
            return MetricUnavailable("0 types analyzed");

        if (metric == BadgeMetric.Mi && summary.MiBearingCount == 0)
            return MetricUnavailable("no method-bearing types (MI undefined)");

        return null;
    }

    internal static BadgeGateResult MetricUnavailable(string detail) =>
        new(GateOutcome.Fail, $"gate failed: metric unavailable ({detail})");

    static BadgeGateResult EvaluateThreshold(
        BadgeMetric metric, StatuslineFormatter.Summary summary, string? failUnder)
    {
        TryParseDouble(failUnder, out var threshold);

        var (actual, gateLabel) = metric == BadgeMetric.CodeHealth
            ? (summary.MinCodeHealth, "min CodeHealth")
            : (summary.AverageMaintainabilityIndex, "average MI");

        return actual < threshold
            ? new BadgeGateResult(GateOutcome.Fail, $"gate failed: {gateLabel} {FormatValue(actual)} < {FormatValue(threshold)}")
            : Pass();
    }

    static BadgeGateResult EvaluateSmells(StatuslineFormatter.Summary summary, string? failOver)
    {
        TryParseInt(failOver, out var threshold);

        if (summary.CriticalCount > 0)
            return new BadgeGateResult(GateOutcome.Fail, $"gate failed: {summary.CriticalCount} critical smell(s)");

        return summary.WarningCount > threshold
            ? new BadgeGateResult(GateOutcome.Fail, $"gate failed: {summary.WarningCount} warning smell(s) > {threshold}")
            : Pass();
    }

    internal static BadgeGateResult Pass() => new(GateOutcome.Pass, null);

    internal static BadgeGateResult UsageError(string message) => new(GateOutcome.UsageError, message);

    static string MetricName(BadgeMetric metric) => metric switch
    {
        BadgeMetric.CodeHealth => "codehealth",
        BadgeMetric.Mi => "mi",
        BadgeMetric.Smells => "smells",
        BadgeMetric.Dup => "dup",
        _ => metric.ToString()
    };

    internal static bool TryParseDouble(string? value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    internal static bool TryParseInt(string? value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    internal static string FormatValue(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
