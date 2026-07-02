using Microsoft.CodeAnalysis;

namespace Unilyze.Incremental;

// Declaration-side surfaces of UsedTypes(T) (design doc §4.1): the full DIT base-chain walk
// (every ancestor, not just the immediate base — a receiver typed as this type can bind to any
// inherited member), the transitive interface list, attribute types, generic constraint types,
// and every member signature type.
internal static class DeclarationUsageCollector
{
    public static void Collect(INamedTypeSymbol selfSymbol, UsedTypeCollection used)
    {
        CollectBaseChain(selfSymbol, used);

        foreach (var iface in selfSymbol.AllInterfaces)
            used.Add(iface);

        CollectAttributes(selfSymbol, used);
        CollectConstraints(selfSymbol.TypeParameters, used);

        foreach (var member in selfSymbol.GetMembers())
            CollectMemberSurface(member, used);
    }

    static void CollectBaseChain(INamedTypeSymbol selfSymbol, UsedTypeCollection used)
    {
        var current = selfSymbol.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            used.Add(current);
            current = current.BaseType;
        }
    }

    static void CollectMemberSurface(ISymbol member, UsedTypeCollection used)
    {
        if (member.IsImplicitlyDeclared)
            return;

        CollectAttributes(member, used);

        switch (member)
        {
            case IMethodSymbol method:
                CollectMethodSurface(method, used);
                break;
            case IPropertySymbol prop:
                used.Add(prop.Type);
                foreach (var p in prop.Parameters)
                    used.Add(p.Type);
                break;
            case IFieldSymbol field:
                used.Add(field.Type);
                break;
            case IEventSymbol ev:
                used.Add(ev.Type);
                break;
        }
    }

    static void CollectMethodSurface(IMethodSymbol method, UsedTypeCollection used)
    {
        used.Add(method.ReturnType);
        foreach (var p in method.Parameters)
            used.Add(p.Type);
        CollectConstraints(method.TypeParameters, used);
    }

    static void CollectAttributes(ISymbol symbol, UsedTypeCollection used)
    {
        foreach (var attr in symbol.GetAttributes())
            used.Add(attr.AttributeClass);
    }

    static void CollectConstraints(
        IReadOnlyList<ITypeParameterSymbol> typeParameters, UsedTypeCollection used)
    {
        foreach (var typeParam in typeParameters)
            foreach (var constraint in typeParam.ConstraintTypes)
                used.Add(constraint);
    }
}
