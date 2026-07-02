using Unilyze.Pipeline;

namespace Unilyze.Incremental;

// InhDesc(B) (design doc §4.3-4.4 Phase B): the transitive inheritance/interface-implementation
// descendant set of a type. Built fresh from the CURRENT generation's declaration dependency
// graph (Inheritance/InterfaceImpl edges from DependencyBuilder, already rebuilt full every
// generation per §4.4) — never persisted, since deps is not persisted either. Rationale for the
// closure (§4.3): a receiver statically typed as a descendant D of B that bound to an inherited
// member can re-bind to B's new/removed member (hiding/capture) or to a shifted DIT/base chain,
// so both D itself and D's callers must be considered alongside B when B's member set or base
// list changes.
internal static class InheritanceDescendantIndex
{
    // Direct parent -> children adjacency (inverts the Inheritance/InterfaceImpl edges, which are
    // recorded child -> parent: FromTypeId is the declaring type, ToTypeId is its base/interface).
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Build(IEnumerable<TypeDependency> deps)
    {
        var childrenByParent = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var dep in deps)
        {
            if (dep.Kind is not (DependencyKind.Inheritance or DependencyKind.InterfaceImpl))
                continue;
            if (dep.FromTypeId is not { } child || dep.ToTypeId is not { } parent)
                continue;

            if (!childrenByParent.TryGetValue(parent, out var list))
            {
                list = [];
                childrenByParent[parent] = list;
            }
            list.Add(child);
        }

        return childrenByParent.ToDictionary(
            kvp => kvp.Key, IReadOnlyList<string> (kvp) => kvp.Value, StringComparer.Ordinal);
    }

    // Transitive descendants of `typeId` (does NOT include typeId itself). Cycle-safe via the
    // visited set, though a well-formed C# inheritance/interface graph is acyclic.
    public static IReadOnlySet<string> DescendantsOf(
        IReadOnlyDictionary<string, IReadOnlyList<string>> childrenByParent, string typeId)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(typeId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!childrenByParent.TryGetValue(current, out var children))
                continue;

            foreach (var child in children)
                if (result.Add(child))
                    stack.Push(child);
        }

        return result;
    }
}
