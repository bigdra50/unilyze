namespace Unilyze;

public static class RankCalculator
{
    const double Damping = 0.85;
    const double ConvergenceThreshold = 1e-6;
    const int MaxIterations = 100;

    public static IReadOnlyDictionary<string, double> CalculateTypeRank(
        IReadOnlyList<TypeDependency> dependencies,
        IReadOnlyList<TypeNodeInfo> allTypes)
    {
        if (allTypes.Count == 0)
            return new Dictionary<string, double>();

        var (typeIds, indexMap) = BuildTypeIndex(allTypes);
        var (outgoing, outDegree) = BuildDependencyGraph(dependencies, indexMap, typeIds.Count);
        var rank = RunPowerIteration(outgoing, outDegree, typeIds.Count);
        return NormalizeRanks(typeIds, rank);
    }

    static (List<string> TypeIds, Dictionary<string, int> IndexMap) BuildTypeIndex(
        IReadOnlyList<TypeNodeInfo> allTypes)
    {
        var typeIds = new List<string>();
        var typeIdSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in allTypes)
        {
            var id = type.TypeId ?? $"{type.Namespace}.{type.Name}";
            if (typeIdSet.Add(id))
                typeIds.Add(id);
        }

        var indexMap = new Dictionary<string, int>(typeIds.Count, StringComparer.Ordinal);
        for (int i = 0; i < typeIds.Count; i++)
            indexMap[typeIds[i]] = i;

        return (typeIds, indexMap);
    }

    // Adjacency: outgoing[from] = distinct to-indices (self-loops excluded).
    static (List<int>[] Outgoing, int[] OutDegree) BuildDependencyGraph(
        IReadOnlyList<TypeDependency> dependencies,
        Dictionary<string, int> indexMap,
        int n)
    {
        var outgoing = new List<int>[n];
        for (int i = 0; i < n; i++)
            outgoing[i] = new List<int>();

        var edgeSet = new HashSet<(int, int)>();

        foreach (var dep in dependencies)
        {
            var fromId = dep.FromTypeId ?? $"{dep.FromType}";
            var toId = dep.ToTypeId ?? $"{dep.ToType}";

            if (!indexMap.TryGetValue(fromId, out var fromIdx)) continue;
            if (!indexMap.TryGetValue(toId, out var toIdx)) continue;
            if (fromIdx == toIdx) continue;

            if (edgeSet.Add((fromIdx, toIdx)))
                outgoing[fromIdx].Add(toIdx);
        }

        var outDegree = new int[n];
        for (int i = 0; i < n; i++)
            outDegree[i] = outgoing[i].Count;

        return (outgoing, outDegree);
    }

    static double[] RunPowerIteration(List<int>[] outgoing, int[] outDegree, int n)
    {
        var rank = new double[n];
        Array.Fill(rank, 1.0 / n);
        var newRank = new double[n];

        for (int iter = 0; iter < MaxIterations; iter++)
        {
            double diff = ComputeNextRanks(outgoing, outDegree, rank, newRank);

            // Swap
            (rank, newRank) = (newRank, rank);

            if (diff < ConvergenceThreshold)
                break;
        }

        return rank;
    }

    // Writes the next iteration into newRank and returns the L1-norm delta.
    static double ComputeNextRanks(List<int>[] outgoing, int[] outDegree, double[] rank, double[] newRank)
    {
        int n = rank.Length;

        double danglingSum = 0.0;
        for (int i = 0; i < n; i++)
        {
            if (outDegree[i] == 0)
                danglingSum += rank[i];
        }

        double basePart = (1.0 - Damping) / n + Damping * danglingSum / n;
        Array.Fill(newRank, basePart);

        for (int i = 0; i < n; i++)
        {
            if (outDegree[i] == 0) continue;
            double contribution = Damping * rank[i] / outDegree[i];
            foreach (var target in outgoing[i])
                newRank[target] += contribution;
        }

        double diff = 0.0;
        for (int i = 0; i < n; i++)
            diff += Math.Abs(newRank[i] - rank[i]);

        return diff;
    }

    // Normalizes so that ranks sum to 1.0; falls back to uniform when the sum collapses.
    static Dictionary<string, double> NormalizeRanks(List<string> typeIds, double[] rank)
    {
        int n = typeIds.Count;
        double sum = 0.0;
        for (int i = 0; i < n; i++)
            sum += rank[i];

        var result = new Dictionary<string, double>(n, StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
            result[typeIds[i]] = sum > 0 ? rank[i] / sum : 1.0 / n;

        return result;
    }
}
