using System.Globalization;

namespace Unilyze.Output;

internal static class BadgeDupGate
{
    internal static BadgeGateResult ValidateOptions(string? failUnder, string? failOver)
    {
        if (failUnder is not null)
            return BadgeGate.UsageError("--fail-under is not valid with --metric dup. Use --fail-over <percent>.");

        if (failOver is null)
            return BadgeGate.Pass();

        if (!BadgeGate.TryParseDouble(failOver, out var percent) || percent < 0)
            return BadgeGate.UsageError($"--fail-over requires a non-negative decimal percent (got '{failOver}').");

        return BadgeGate.Pass();
    }

    internal static BadgeGateResult Evaluate(double? duplicationPercent, string? failOver)
    {
        if (duplicationPercent is null)
            return BadgeGate.MetricUnavailable("no files analyzed");

        BadgeGate.TryParseDouble(failOver, out var threshold);
        return duplicationPercent.Value > threshold
            ? new BadgeGateResult(
                GateOutcome.Fail,
                $"gate failed: duplication {BadgeGate.FormatValue(duplicationPercent.Value)}% > {BadgeGate.FormatValue(threshold)}%")
            : BadgeGate.Pass();
    }
}
