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

        return new TarjanScc(adjacency).FindCycles()
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

        return new TarjanScc(adjacency).FindCycles()
            .Select(scc => new CyclicDependency(scc, CycleLevel.Assembly))
            .ToList();
    }

    internal static IReadOnlyList<IReadOnlyList<string>> TarjanSCC(
        Dictionary<string, List<string>> adjacency) => new TarjanScc(adjacency).FindCycles();
}

// Tarjan's strongly-connected-components algorithm (recursive lowlink form).
sealed class TarjanScc(Dictionary<string, List<string>> adjacency)
{
    readonly Stack<string> _stack = new();
    readonly HashSet<string> _onStack = [];
    readonly Dictionary<string, int> _indices = [];
    readonly Dictionary<string, int> _lowlinks = [];
    readonly List<IReadOnlyList<string>> _cycles = [];
    int _index;

    public IReadOnlyList<IReadOnlyList<string>> FindCycles()
    {
        foreach (var node in adjacency.Keys)
        {
            if (!_indices.ContainsKey(node))
                StrongConnect(node);
        }

        return _cycles;
    }

    void StrongConnect(string v)
    {
        _indices[v] = _index;
        _lowlinks[v] = _index;
        _index++;
        _stack.Push(v);
        _onStack.Add(v);

        VisitNeighbors(v);

        if (_lowlinks[v] == _indices[v])
            CollectCycle(v);
    }

    void VisitNeighbors(string v)
    {
        if (!adjacency.TryGetValue(v, out var neighbors))
            return;

        foreach (var w in neighbors)
        {
            if (_indices.ContainsKey(w))
            {
                if (_onStack.Contains(w))
                    _lowlinks[v] = Math.Min(_lowlinks[v], _indices[w]);
                continue;
            }

            if (!adjacency.ContainsKey(w))
                continue;

            StrongConnect(w);
            _lowlinks[v] = Math.Min(_lowlinks[v], _lowlinks[w]);
        }
    }

    // Pops the SCC rooted at v; only SCCs with more than one node are actual cycles.
    void CollectCycle(string v)
    {
        var scc = new List<string>();
        string w;
        do
        {
            w = _stack.Pop();
            _onStack.Remove(w);
            scc.Add(w);
        } while (w != v);

        if (scc.Count > 1)
            _cycles.Add(scc);
    }
}
