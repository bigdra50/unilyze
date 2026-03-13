namespace Unilyze;

public enum CycleLevel { Type, Assembly }

public sealed record CyclicDependency(
    IReadOnlyList<string> Cycle,
    CycleLevel Level);

public static class CycleDetector
{
    public static IReadOnlyList<CyclicDependency> DetectAll(
        IReadOnlyList<TypeDependency> dependencies,
        IReadOnlyList<AssemblyInfo> assemblies)
    {
        var results = new List<CyclicDependency>();
        results.AddRange(DetectTypeCycles(dependencies));
        results.AddRange(DetectAssemblyCycles(assemblies));
        return results;
    }

    public static IReadOnlyList<CyclicDependency> DetectTypeCycles(
        IReadOnlyList<TypeDependency> dependencies)
    {
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var dep in dependencies)
        {
            if (!adjacency.TryGetValue(dep.FromType, out var list))
            {
                list = [];
                adjacency[dep.FromType] = list;
            }
            if (!list.Contains(dep.ToType))
                list.Add(dep.ToType);

            adjacency.TryAdd(dep.ToType, []);
        }

        return TarjanSCC(adjacency)
            .Select(scc => new CyclicDependency(scc, CycleLevel.Type))
            .ToList();
    }

    public static IReadOnlyList<CyclicDependency> DetectAssemblyCycles(
        IReadOnlyList<AssemblyInfo> assemblies)
    {
        var names = new HashSet<string>(assemblies.Select(a => a.Name));
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var asm in assemblies)
        {
            var refs = asm.References
                .Where(r => names.Contains(r))
                .Distinct()
                .ToList();
            adjacency[asm.Name] = refs;
        }

        return TarjanSCC(adjacency)
            .Select(scc => new CyclicDependency(scc, CycleLevel.Assembly))
            .ToList();
    }

    internal static IReadOnlyList<IReadOnlyList<string>> TarjanSCC(
        Dictionary<string, List<string>> adjacency)
    {
        var index = 0;
        var stack = new Stack<string>();
        var onStack = new HashSet<string>();
        var indices = new Dictionary<string, int>();
        var lowlinks = new Dictionary<string, int>();
        var result = new List<IReadOnlyList<string>>();

        foreach (var node in adjacency.Keys)
        {
            if (!indices.ContainsKey(node))
                StrongConnect(node);
        }

        return result;

        void StrongConnect(string v)
        {
            indices[v] = index;
            lowlinks[v] = index;
            index++;
            stack.Push(v);
            onStack.Add(v);

            if (adjacency.TryGetValue(v, out var neighbors))
            {
                foreach (var w in neighbors)
                {
                    if (!indices.ContainsKey(w))
                    {
                        if (adjacency.ContainsKey(w))
                        {
                            StrongConnect(w);
                            lowlinks[v] = Math.Min(lowlinks[v], lowlinks[w]);
                        }
                    }
                    else if (onStack.Contains(w))
                    {
                        lowlinks[v] = Math.Min(lowlinks[v], indices[w]);
                    }
                }
            }

            if (lowlinks[v] == indices[v])
            {
                var scc = new List<string>();
                string w;
                do
                {
                    w = stack.Pop();
                    onStack.Remove(w);
                    scc.Add(w);
                } while (w != v);

                // Only keep SCCs with more than 1 node (actual cycles)
                if (scc.Count > 1)
                    result.Add(scc);
            }
        }
    }
}
