using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Detectors;

internal static class EcsInterfaceMatcher
{
    internal static bool IsEcsSystem(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (typeDecl is not StructDeclarationSyntax)
            return false;

        if (model?.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol symbol)
            return ImplementsUnityEntitiesInterface(symbol, "ISystem");

        return ImplementsInterfaceSyntax(typeDecl, "ISystem");
    }

    internal static bool IsEcsJob(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (typeDecl is not StructDeclarationSyntax)
            return false;

        if (model?.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol symbol)
            return ImplementsUnityEntitiesInterface(symbol, "IJobEntity", "IJobChunk");

        return ImplementsInterfaceSyntax(typeDecl, "IJobEntity", "IJobChunk");
    }

    internal static bool IsEcsComponentData(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (typeDecl is not StructDeclarationSyntax)
            return false;

        if (model?.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol symbol)
            return ImplementsUnityEntitiesInterface(symbol, "IComponentData");

        return ImplementsInterfaceSyntax(typeDecl, "IComponentData");
    }

    internal static bool ImplementsInterfaceName(IReadOnlyList<string> interfaces, string interfaceName)
    {
        foreach (var iface in interfaces)
        {
            var segment = iface.Split('<')[0].Split('.')[^1];
            if (segment == interfaceName)
                return true;
        }

        return false;
    }

    internal static bool ImplementsJobInterfaceName(IReadOnlyList<string> interfaces)
        => ImplementsInterfaceName(interfaces, "IJobEntity")
           || ImplementsInterfaceName(interfaces, "IJobChunk");

    static bool ImplementsUnityEntitiesInterface(INamedTypeSymbol symbol, params string[] interfaceNames)
    {
        foreach (var iface in EnumerateInterfaces(symbol))
        {
            if (!interfaceNames.Contains(iface.Name))
                continue;
            if (IsUnityEntitiesType(iface))
                return true;
        }

        return false;
    }

    static IEnumerable<INamedTypeSymbol> EnumerateInterfaces(INamedTypeSymbol symbol)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var iface in symbol.Interfaces)
        {
            if (seen.Add(iface))
                yield return iface;
        }

        foreach (var iface in symbol.AllInterfaces)
        {
            if (seen.Add(iface))
                yield return iface;
        }
    }

    static bool ImplementsInterfaceSyntax(TypeDeclarationSyntax typeDecl, params string[] interfaceNames)
    {
        if (typeDecl.BaseList is null)
            return false;

        foreach (var baseType in typeDecl.BaseList.Types)
        {
            var segment = GetLastIdentifierSegment(baseType.Type);
            if (segment is not null && interfaceNames.Contains(segment))
                return true;
        }

        return false;
    }

    static bool IsUnityEntitiesType(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace)
            return false;

        if (ns.Name == "Entities" && ns.ContainingNamespace?.Name == "Unity")
            return true;

        var display = ns.ToDisplayString();
        return display is "Unity.Entities" or "global::Unity.Entities";
    }

    static string? GetLastIdentifierSegment(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax qual => qual.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            GenericNameSyntax gen => gen.Identifier.Text,
            _ => null
        };
    }
}
