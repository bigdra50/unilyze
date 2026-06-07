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
    /// <summary>
    /// Validates the gate option combination without needing analysis results.
    /// Returns a UsageError outcome for incompatible flags (e.g. --fail-under on
    /// metric smells), otherwise Pass. Lets the runner fail fast before analysis.
    /// </summary>
    internal static BadgeGateResult ValidateOptions(BadgeMetric metric, string? failUnder, string? failOver)
    {
        var hasFailUnder = failUnder is not null;
        var hasFailOver = failOver is not null;

        if (!hasFailUnder && !hasFailOver)
            return new BadgeGateResult(GateOutcome.Pass, null);

        switch (metric)
        {
            case BadgeMetric.CodeHealth or BadgeMetric.Mi:
                if (hasFailOver)
                    return UsageError(
                        $"--fail-over is only valid with --metric smells. Use --fail-under for metric '{MetricName(metric)}'.");
                if (!TryParseDouble(failUnder, out _))
                    return UsageError($"--fail-under requires a numeric value (got '{failUnder}').");
                return Pass();
            case BadgeMetric.Smells:
                if (hasFailUnder)
                    return UsageError("--fail-under is not valid with --metric smells. Use --fail-over <count>.");
                if (!TryParseInt(failOver, out var count) || count < 0)
                    return UsageError($"--fail-over requires a non-negative integer value (got '{failOver}').");
                return Pass();
            default:
                return UsageError($"Unsupported metric: {metric}");
        }
    }

    /// <param name="metric">Badge metric the gate is evaluated against.</param>
    /// <param name="summary">Computed project summary.</param>
    /// <param name="failUnder">Raw value of --fail-under, or null when absent.</param>
    /// <param name="failOver">Raw value of --fail-over, or null when absent.</param>
    internal static BadgeGateResult Evaluate(
        BadgeMetric metric, StatuslineFormatter.Summary summary, string? failUnder, string? failOver)
    {
        var validation = ValidateOptions(metric, failUnder, failOver);
        if (validation.Outcome != GateOutcome.Pass)
            return validation;

        var hasFailUnder = failUnder is not null;
        var hasFailOver = failOver is not null;

        if (!hasFailUnder && !hasFailOver)
            return Pass();

        return metric switch
        {
            BadgeMetric.CodeHealth or BadgeMetric.Mi => EvaluateThreshold(metric, summary, failUnder, hasFailOver),
            BadgeMetric.Smells => EvaluateSmells(summary, failOver, hasFailUnder),
            _ => UsageError($"Unsupported metric: {metric}")
        };
    }

    static BadgeGateResult EvaluateThreshold(
        BadgeMetric metric, StatuslineFormatter.Summary summary, string? failUnder, bool hasFailOver)
    {
        // Combination and value validity are already guaranteed by ValidateOptions.
        TryParseDouble(failUnder, out var threshold);

        // metric=codehealth gates on the worst (min) type; metric=mi gates on the average.
        var (actual, gateLabel) = metric == BadgeMetric.CodeHealth
            ? (summary.MinCodeHealth, "min CodeHealth")
            : (summary.AverageMaintainabilityIndex, "average MI");

        // Boundary: exactly the threshold passes (only strictly-below fails).
        return actual < threshold
            ? new BadgeGateResult(GateOutcome.Fail, $"gate failed: {gateLabel} {Fmt(actual)} < {Fmt(threshold)}")
            : Pass();
    }

    static BadgeGateResult EvaluateSmells(
        StatuslineFormatter.Summary summary, string? failOver, bool hasFailUnder)
    {
        // Combination and value validity are already guaranteed by ValidateOptions.
        TryParseInt(failOver, out var threshold);

        // Any critical smell fails regardless of the warning threshold.
        if (summary.CriticalCount > 0)
            return new BadgeGateResult(GateOutcome.Fail,
                $"gate failed: {summary.CriticalCount} critical smell(s)");

        // Boundary: exactly the threshold passes (only strictly-over fails).
        return summary.WarningCount > threshold
            ? new BadgeGateResult(GateOutcome.Fail, $"gate failed: {summary.WarningCount} warning smell(s) > {threshold}")
            : Pass();
    }

    static BadgeGateResult Pass() => new(GateOutcome.Pass, null);

    static BadgeGateResult UsageError(string message) => new(GateOutcome.UsageError, message);

    static string MetricName(BadgeMetric metric) => metric switch
    {
        BadgeMetric.CodeHealth => "codehealth",
        BadgeMetric.Mi => "mi",
        BadgeMetric.Smells => "smells",
        _ => metric.ToString()
    };

    static bool TryParseDouble(string? value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    static bool TryParseInt(string? value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    static string Fmt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
