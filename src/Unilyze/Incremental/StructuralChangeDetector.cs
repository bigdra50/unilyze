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
    // verdict into "which types in this file changed, and how". Type add/delete within the file
    // (and the defensive multiple-declarations-per-TypeId case) still demand the full-fallback
    // (RequiresFull = true). Everything else on an EXISTING type resolves to one of three precise
    // delta classes, resolved by the caller through RDeps/InhDesc instead of re-enriching
    // everything:
    //   - Δsig: member SET and base/interface list both intact (a pure signature modify —
    //     parameter/return type, modifiers, attributes, generic constraints);
    //   - Δmembers: the member SET changed (add/remove/overload add-or-remove) or the primary-
    //     constructor parameter list changed (treated as a member-set change per design doc §4.3
    //     Phase B: "ConstructorParams change ... downgraded to Δmembers");
    //   - Δbase: the base type or interface list changed.
    // A type can be BOTH Δmembers and Δbase in the same generation (e.g. a member added AND the
    // base type changed) — both lists get the TypeId, and the caller's union of their
    // invalidation sets is still sound (Δbase's is already a superset of Δmembers's for the same
    // B). Operates on raw (syntax-level) TypeNodeInfo only — no SemanticModel involved.
    public readonly record struct FileTypeDeltaResult(
        bool RequiresFull,
        string? FullReason,
        IReadOnlyList<string> SigChangedTypeIds,
        IReadOnlyList<string> MembersChangedTypeIds,
        IReadOnlyList<string> BaseChangedTypeIds);

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
            return new FileTypeDeltaResult(true, "multiple declarations share a TypeId in this file", [], [], []);

        var previousByTypeId = previousGroups.ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var currentByTypeId = currentGroups.ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        if (previousByTypeId.Keys.Any(id => !currentByTypeId.ContainsKey(id)))
            return new FileTypeDeltaResult(true, "a type was removed", [], [], []);
        if (currentByTypeId.Keys.Any(id => !previousByTypeId.ContainsKey(id)))
            return new FileTypeDeltaResult(true, "a type was added", [], [], []);

        var sigChanged = new List<string>();
        var membersChanged = new List<string>();
        var baseChanged = new List<string>();
        foreach (var (typeId, before) in previousByTypeId)
        {
            var after = currentByTypeId[typeId];
            if (TypeStructureSignature(before) == TypeStructureSignature(after))
                continue; // body-only for this type: no declaration-shape change at all

            var memberSetChanged = MemberSetChanged(before, after)
                || !before.ConstructorParams.SequenceEqual(after.ConstructorParams, StringComparer.Ordinal);
            var baseOrIfaceChanged = BaseOrInterfacesChanged(before, after);

            if (memberSetChanged)
            {
                // Extension methods are `this`-first-parameter methods declared inside a static
                // class; the design doc's precise Δmembers rule additionally invalidates
                // RDeps(P ∪ InhDesc(P)) for the receiver type P when the changed member is an
                // extension method. ParameterInfo (the raw syntax-level parameter model) carries
                // no `this`-modifier flag, so that case can't be distinguished syntactically here
                // without widening the raw parser model. Conservative deviation (documented in
                // the design doc / PR description): any member-set change on a static class stays
                // a full re-enrich rather than guessing whether it touched an extension method.
                if (IsStaticClass(after))
                    return new FileTypeDeltaResult(
                        true, "a static class's member set changed (possible extension method)", [], [], []);
                membersChanged.Add(typeId);
            }

            if (baseOrIfaceChanged)
                baseChanged.Add(typeId);

            if (!memberSetChanged && !baseOrIfaceChanged)
                sigChanged.Add(typeId);
        }

        return new FileTypeDeltaResult(false, null, sigChanged, membersChanged, baseChanged);
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

    // C# static classes cannot declare a base type (other than object) or implement interfaces,
    // so this never overlaps with a Δbase classification.
    static bool IsStaticClass(TypeNodeInfo type) =>
        type.Kind == "class" && type.Modifiers.Contains("static");
}
