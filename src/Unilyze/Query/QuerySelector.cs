using Unilyze.Metrics;
using Unilyze.Pipeline;
namespace Unilyze.Query;

internal static class QuerySelector
{
    internal sealed record SelectionResult(
        IReadOnlyList<TypeMetrics> Types,
        string? AmbiguityMessage = null);

    public static SelectionResult SelectWorst(IReadOnlyList<TypeMetrics> metrics, int count)
    {
        var selected = metrics
            .OrderBy(t => t.CodeHealth)
            .ThenBy(t => TypeIdentity.GetQualifiedName(t), StringComparer.Ordinal)
            .Take(count)
            .ToList();
        return new SelectionResult(selected);
    }

    public static SelectionResult SelectByName(IReadOnlyList<TypeMetrics> metrics, string name)
    {
        var matches = metrics
            .Where(t => MatchesName(t, name))
            .ToList();

        if (matches.Count == 0)
            return new SelectionResult([], $"Type not found: '{name}'");

        if (matches.Count > 1)
        {
            var candidates = matches
                .Select(t => TypeIdentity.GetQualifiedName(t))
                .OrderBy(n => n, StringComparer.Ordinal);
            var message = $"Ambiguous type name '{name}'. Candidates:{Environment.NewLine}"
                + string.Join(Environment.NewLine, candidates.Select(c => $"  {c}"));
            return new SelectionResult([], message);
        }

        return new SelectionResult([matches[0]]);
    }

    static bool MatchesName(TypeMetrics type, string name) =>
        type.TypeName.Equals(name, StringComparison.OrdinalIgnoreCase)
        || TypeIdentity.GetQualifiedName(type).Equals(name, StringComparison.OrdinalIgnoreCase);
}
