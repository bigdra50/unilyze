using Unilyze.Pipeline;
using System.Text;

namespace Unilyze.Incremental;

// Decides whether a warm semantic-incremental generation may re-enrich only the edited
// types (the body-only fast path) or must re-enrich every type (the structural fallback).
//
// Why the split is correct: an UNCHANGED type's semantic metrics (Cbo/Dit/Rfc/Lcom/smells)
// can only change if a type it references changes identity or public surface. A pure
// method-BODY edit never alters another type's surface, so unchanged types keep their cached
// metrics. Any change to a type's declaration shape (members, base/interfaces, modifiers,
// generic constraints, attributes, the type set in a file), a file add/delete, or a global
// using change can shift name resolution for body-callers the declaration dependency graph
// does not capture — so those force a full re-enrich (no worse than a non-incremental run).
internal static class StructuralChangeDetector
{
    // Canonical, order-insensitive signature of a type's declaration shape. Excludes
    // body-derived data (complexity, Halstead, line counts, source positions) so that a
    // pure body edit produces an identical signature.
    public static string TypeStructureSignature(TypeNodeInfo type)
    {
        var builder = new StringBuilder(256);
        builder.Append(TypeIdentity.GetTypeId(type)).Append('␟');
        builder.Append(type.Kind).Append('␟');
        builder.Append(type.BaseType ?? string.Empty).Append('␟');
        builder.Append(type.EnumBaseType ?? string.Empty).Append('␟');
        AppendSorted(builder, type.Modifiers);
        AppendSorted(builder, type.Interfaces);
        AppendSorted(builder, type.ConstructorParams);
        AppendSorted(builder, type.Attributes.Select(a => a.Name));
        AppendSorted(builder, type.GenericConstraints.Select(
            g => $"{g.TypeParameter}:{string.Join(',', g.Constraints.OrderBy(c => c, StringComparer.Ordinal))}"));
        AppendSorted(builder, type.Members.Select(MemberSignature));
        return builder.ToString();
    }

    static string MemberSignature(MemberInfo member)
    {
        var parameters = string.Join(',', member.Parameters.Select(p => $"{p.Name}:{p.Type}"));
        var modifiers = string.Join(',', member.Modifiers.OrderBy(m => m, StringComparer.Ordinal));
        var attributes = string.Join(',', member.Attributes.Select(a => a.Name).OrderBy(a => a, StringComparer.Ordinal));
        return $"{member.Name}|{member.MemberKind}|{member.Type}|{modifiers}|{parameters}|{attributes}";
    }

    // True when the set of declaration signatures in a file differs between two parses
    // (a member/type added, removed, or re-typed), i.e. anything beyond a body edit.
    public static bool FileStructureChanged(
        IReadOnlyList<TypeNodeInfo> previous,
        IReadOnlyList<TypeNodeInfo> current)
        => !BuildFileSignature(previous).SequenceEqual(BuildFileSignature(current), StringComparer.Ordinal);

    static IReadOnlyList<string> BuildFileSignature(IReadOnlyList<TypeNodeInfo> types)
        => types.Select(TypeStructureSignature).OrderBy(s => s, StringComparer.Ordinal).ToList();

    static void AppendSorted(StringBuilder builder, IEnumerable<string> values)
    {
        builder.AppendJoin('␞', values.OrderBy(v => v, StringComparer.Ordinal));
        builder.Append('␟');
    }

    // Per-TypeId delta classification (design doc §4.3): refines the binary FileStructureChanged
    // verdict into "which types in this file changed, and how". Member add/remove, base/interface
    // change, and type add/delete within the file all still demand the full-fallback (RequiresFull
    // = true) — only a signature change that leaves the member SET and base/interface list intact
    // (a pure signature modify: parameter/return type, modifiers, attributes, generic constraints)
    // downgrades to Δsig, which the caller resolves through RDeps(B) instead of re-enriching
    // everything. Operates on raw (syntax-level) TypeNodeInfo only — no SemanticModel involved.
    public readonly record struct FileTypeDeltaResult(
        bool RequiresFull,
        string? FullReason,
        IReadOnlyList<string> SigChangedTypeIds);

    public static FileTypeDeltaResult ClassifyFileTypeDelta(
        IReadOnlyList<TypeNodeInfo> previous,
        IReadOnlyList<TypeNodeInfo> current)
    {
        var previousGroups = previous.GroupBy(TypeIdentity.GetTypeId, StringComparer.Ordinal).ToList();
        var currentGroups = current.GroupBy(TypeIdentity.GetTypeId, StringComparer.Ordinal).ToList();

        // Defensive: two declarations sharing a TypeId within one file's raw types shouldn't
        // happen (partial types normally span files), but if it does there is no sound way to
        // pair "before" with "after" per-declaration, so fall back rather than guess.
        if (previousGroups.Any(g => g.Count() > 1) || currentGroups.Any(g => g.Count() > 1))
            return new FileTypeDeltaResult(true, "multiple declarations share a TypeId in this file", []);

        var previousByTypeId = previousGroups.ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var currentByTypeId = currentGroups.ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        if (previousByTypeId.Keys.Any(id => !currentByTypeId.ContainsKey(id)))
            return new FileTypeDeltaResult(true, "a type was removed", []);
        if (currentByTypeId.Keys.Any(id => !previousByTypeId.ContainsKey(id)))
            return new FileTypeDeltaResult(true, "a type was added", []);

        var sigChanged = new List<string>();
        foreach (var (typeId, before) in previousByTypeId)
        {
            var after = currentByTypeId[typeId];
            if (TypeStructureSignature(before) == TypeStructureSignature(after))
                continue; // body-only for this type: no declaration-shape change at all

            if (MemberSetChanged(before, after))
                return new FileTypeDeltaResult(true, "a member was added or removed", []);
            if (BaseOrInterfacesChanged(before, after))
                return new FileTypeDeltaResult(true, "base type or interfaces changed", []);
            if (!before.ConstructorParams.SequenceEqual(after.ConstructorParams, StringComparer.Ordinal))
                return new FileTypeDeltaResult(true, "a member was added or removed", []);

            sigChanged.Add(typeId);
        }

        return new FileTypeDeltaResult(false, null, sigChanged);
    }

    // Multiset (not set) comparison of (Name|MemberKind) — an overload add/remove changes the
    // multiset even though a same-named/same-kind member remains, so it correctly counts as a
    // member-set change (design doc §4.3: "オーバーロード追加・削除はメンバー集合変化とみなす").
    static bool MemberSetChanged(TypeNodeInfo before, TypeNodeInfo after)
    {
        var beforeKeys = before.Members.Select(MemberSetKey).OrderBy(k => k, StringComparer.Ordinal);
        var afterKeys = after.Members.Select(MemberSetKey).OrderBy(k => k, StringComparer.Ordinal);
        return !beforeKeys.SequenceEqual(afterKeys, StringComparer.Ordinal);
    }

    static string MemberSetKey(MemberInfo member) => $"{member.Name}|{member.MemberKind}";

    static bool BaseOrInterfacesChanged(TypeNodeInfo before, TypeNodeInfo after) =>
        !string.Equals(before.BaseType, after.BaseType, StringComparison.Ordinal)
        || !string.Equals(before.EnumBaseType, after.EnumBaseType, StringComparison.Ordinal)
        || !before.Interfaces.OrderBy(i => i, StringComparer.Ordinal)
            .SequenceEqual(after.Interfaces.OrderBy(i => i, StringComparer.Ordinal), StringComparer.Ordinal);
}
