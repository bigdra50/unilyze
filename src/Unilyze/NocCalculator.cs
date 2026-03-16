namespace Unilyze;

public static class NocCalculator
{
    public static IReadOnlyDictionary<string, int> Calculate(IReadOnlyList<TypeDependency> dependencies)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var dep in dependencies)
        {
            if (dep.Kind != DependencyKind.Inheritance)
                continue;

            var parentId = dep.ToTypeId ?? dep.ToType;
            if (!counts.TryGetValue(parentId, out _))
                counts[parentId] = 0;
            counts[parentId]++;
        }

        return counts;
    }
}
