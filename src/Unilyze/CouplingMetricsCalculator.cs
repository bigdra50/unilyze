using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Unilyze;

public sealed record CouplingInfo(int AfferentCoupling, int EfferentCoupling, double? Instability);

public static class CouplingMetricsCalculator
{
    public static IReadOnlyDictionary<string, CouplingInfo> Calculate(
        IReadOnlyList<TypeDependency> dependencies,
        IReadOnlyList<TypeNodeInfo> allTypes)
    {
        var allTypeNames = new HashSet<string>(allTypes.Select(t => t.Name));
        var (ceCount, caCount) = CountCouplings(dependencies, allTypeNames);
        return BuildResult(allTypeNames, ceCount, caCount);
    }

    static (Dictionary<string, int> Ce, Dictionary<string, int> Ca) CountCouplings(
        IReadOnlyList<TypeDependency> dependencies, HashSet<string> allTypeNames)
    {
        var ceCount = new Dictionary<string, int>(allTypeNames.Count);
        var caCount = new Dictionary<string, int>(allTypeNames.Count);
        foreach (var name in allTypeNames)
        {
            ceCount[name] = 0;
            caCount[name] = 0;
        }

        var seen = new HashSet<(string From, string To)>();
        foreach (var dep in dependencies)
        {
            if (!allTypeNames.Contains(dep.FromType) || !allTypeNames.Contains(dep.ToType))
                continue;
            if (dep.FromType == dep.ToType || !seen.Add((dep.FromType, dep.ToType)))
                continue;

            ref var ceRef = ref CollectionsMarshal.GetValueRefOrNullRef(ceCount, dep.FromType);
            if (!Unsafe.IsNullRef(ref ceRef)) ceRef++;

            ref var caRef = ref CollectionsMarshal.GetValueRefOrNullRef(caCount, dep.ToType);
            if (!Unsafe.IsNullRef(ref caRef)) caRef++;
        }

        return (ceCount, caCount);
    }

    static Dictionary<string, CouplingInfo> BuildResult(
        HashSet<string> allTypeNames, Dictionary<string, int> ceCount, Dictionary<string, int> caCount)
    {
        var result = new Dictionary<string, CouplingInfo>(allTypeNames.Count);
        foreach (var name in allTypeNames)
        {
            var ce = ceCount[name];
            var ca = caCount[name];
            var total = ca + ce;
            var instability = total > 0 ? (double)ce / total : (double?)null;
            result[name] = new CouplingInfo(ca, ce, instability);
        }

        return result;
    }
}
