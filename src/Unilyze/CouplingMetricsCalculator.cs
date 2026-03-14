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
        var allTypeIds = new HashSet<string>(allTypes.Select(TypeIdentity.GetTypeId));
        var (ceCount, caCount) = CountCouplings(dependencies, allTypeIds);
        return BuildResult(allTypeIds, ceCount, caCount);
    }

    static (Dictionary<string, int> Ce, Dictionary<string, int> Ca) CountCouplings(
        IReadOnlyList<TypeDependency> dependencies, HashSet<string> allTypeIds)
    {
        var ceCount = new Dictionary<string, int>(allTypeIds.Count);
        var caCount = new Dictionary<string, int>(allTypeIds.Count);
        foreach (var typeId in allTypeIds)
        {
            ceCount[typeId] = 0;
            caCount[typeId] = 0;
        }

        var seen = new HashSet<(string From, string To)>();
        foreach (var dep in dependencies)
        {
            if (dep.FromTypeId is null || dep.ToTypeId is null)
                continue;
            if (!allTypeIds.Contains(dep.FromTypeId) || !allTypeIds.Contains(dep.ToTypeId))
                continue;
            if (dep.FromTypeId == dep.ToTypeId || !seen.Add((dep.FromTypeId, dep.ToTypeId)))
                continue;

            ref var ceRef = ref CollectionsMarshal.GetValueRefOrNullRef(ceCount, dep.FromTypeId);
            if (!Unsafe.IsNullRef(ref ceRef)) ceRef++;

            ref var caRef = ref CollectionsMarshal.GetValueRefOrNullRef(caCount, dep.ToTypeId);
            if (!Unsafe.IsNullRef(ref caRef)) caRef++;
        }

        return (ceCount, caCount);
    }

    static Dictionary<string, CouplingInfo> BuildResult(
        HashSet<string> allTypeIds, Dictionary<string, int> ceCount, Dictionary<string, int> caCount)
    {
        var result = new Dictionary<string, CouplingInfo>(allTypeIds.Count);
        foreach (var typeId in allTypeIds)
        {
            var ce = ceCount[typeId];
            var ca = caCount[typeId];
            var total = ca + ce;
            var instability = total > 0 ? (double)ce / total : (double?)null;
            result[typeId] = new CouplingInfo(ca, ce, instability);
        }

        return result;
    }
}
