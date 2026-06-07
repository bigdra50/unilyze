using System.Globalization;

namespace Unilyze;

internal sealed record DiffGateResult(bool HasRegression, string? Reason);

/// <summary>
/// Pure evaluation of the diff regression gate (--fail-on-regression).
/// A regression is any of: avg or min CodeHealth dropped, warning smells
/// increased, or critical smells increased (after vs before).
/// </summary>
internal static class DiffGate
{
    // Tolerance for CodeHealth comparison; matches DiffCalculator's epsilon.
    const double Epsilon = 0.0001;

    internal static DiffGateResult EvaluateRegression(
        StatuslineFormatter.Summary before, StatuslineFormatter.Summary after)
    {
        if (after.MinCodeHealth < before.MinCodeHealth - Epsilon)
            return Regression($"min CodeHealth {Fmt(before.MinCodeHealth)} -> {Fmt(after.MinCodeHealth)}");

        if (after.AverageCodeHealth < before.AverageCodeHealth - Epsilon)
            return Regression($"avg CodeHealth {Fmt(before.AverageCodeHealth)} -> {Fmt(after.AverageCodeHealth)}");

        if (after.CriticalCount > before.CriticalCount)
            return Regression($"critical smells {before.CriticalCount} -> {after.CriticalCount}");

        if (after.WarningCount > before.WarningCount)
            return Regression($"warning smells {before.WarningCount} -> {after.WarningCount}");

        return new DiffGateResult(HasRegression: false, Reason: null);
    }

    static DiffGateResult Regression(string detail) =>
        new(HasRegression: true, Reason: $"regression: {detail}");

    // Matches BadgeGate.Fmt so both gates emit identical numeric precision in reasons.
    static string Fmt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
