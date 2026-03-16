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

        var typeIds = new List<string>();
        var typeIdSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in allTypes)
        {
            var id = type.TypeId ?? $"{type.Namespace}.{type.Name}";
            if (typeIdSet.Add(id))
                typeIds.Add(id);
        }

        int n = typeIds.Count;
        var indexMap = new Dictionary<string, int>(n, StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
            indexMap[typeIds[i]] = i;

        // Build adjacency: outgoing[from] = list of to-indices
        var outgoing = new List<int>[n];
        for (int i = 0; i < n; i++)
            outgoing[i] = new List<int>();

        // incoming[to] = list of from-indices
        var incoming = new List<int>[n];
        for (int i = 0; i < n; i++)
            incoming[i] = new List<int>();

        var edgeSet = new HashSet<(int, int)>();

        foreach (var dep in dependencies)
        {
            var fromId = dep.FromTypeId ?? $"{dep.FromType}";
            var toId = dep.ToTypeId ?? $"{dep.ToType}";

            if (!indexMap.TryGetValue(fromId, out var fromIdx)) continue;
            if (!indexMap.TryGetValue(toId, out var toIdx)) continue;
            if (fromIdx == toIdx) continue;

            if (edgeSet.Add((fromIdx, toIdx)))
            {
                outgoing[fromIdx].Add(toIdx);
                incoming[toIdx].Add(fromIdx);
            }
        }

        // Compute out-degree
        var outDegree = new int[n];
        for (int i = 0; i < n; i++)
            outDegree[i] = outgoing[i].Count;

        // Initialize ranks
        double initialRank = 1.0 / n;
        var rank = new double[n];
        for (int i = 0; i < n; i++)
            rank[i] = initialRank;

        var newRank = new double[n];

        // Power iteration
        for (int iter = 0; iter < MaxIterations; iter++)
        {
            // Calculate dangling node contribution
            double danglingSum = 0.0;
            for (int i = 0; i < n; i++)
            {
                if (outDegree[i] == 0)
                    danglingSum += rank[i];
            }

            double basePart = (1.0 - Damping) / n + Damping * danglingSum / n;

            for (int i = 0; i < n; i++)
                newRank[i] = basePart;

            for (int i = 0; i < n; i++)
            {
                if (outDegree[i] == 0) continue;
                double contribution = Damping * rank[i] / outDegree[i];
                foreach (var target in outgoing[i])
                    newRank[target] += contribution;
            }

            // Check convergence (L1 norm)
            double diff = 0.0;
            for (int i = 0; i < n; i++)
                diff += Math.Abs(newRank[i] - rank[i]);

            // Swap
            (rank, newRank) = (newRank, rank);

            if (diff < ConvergenceThreshold)
                break;
        }

        // Normalize so that sum = 1.0
        double sum = 0.0;
        for (int i = 0; i < n; i++)
            sum += rank[i];

        var result = new Dictionary<string, double>(n, StringComparer.Ordinal);
        if (sum > 0)
        {
            for (int i = 0; i < n; i++)
                result[typeIds[i]] = rank[i] / sum;
        }
        else
        {
            for (int i = 0; i < n; i++)
                result[typeIds[i]] = 1.0 / n;
        }

        return result;
    }
}
