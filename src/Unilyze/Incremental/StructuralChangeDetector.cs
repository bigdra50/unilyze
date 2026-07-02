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
}
