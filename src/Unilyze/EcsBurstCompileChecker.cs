using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

internal static class EcsBurstCompileChecker
{
    static readonly HashSet<string> SystemLifecycleMethods = new(StringComparer.Ordinal)
    {
        "OnCreate", "OnUpdate", "OnDestroy"
    };

    public static bool IsBurstCovered(TypeNodeInfo type)
    {
        if (type.Role is not (TypeRole.EcsSystem or TypeRole.EcsJob))
            return false;

        if (HasBurstCompile(type.Attributes))
            return true;

        if (type.Role != TypeRole.EcsSystem)
            return false;

        var lifecycleMethods = type.Members
            .Where(m => m.MemberKind == "Method" && SystemLifecycleMethods.Contains(m.Name))
            .ToList();
        if (lifecycleMethods.Count == 0)
            return false;

        return lifecycleMethods.All(m => HasBurstCompile(m.Attributes));
    }

    public static bool IsMissingBurstCompile(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (EcsInterfaceMatcher.IsEcsJob(typeDecl, model))
            return !HasBurstCompileOnType(typeDecl, model);

        if (!EcsInterfaceMatcher.IsEcsSystem(typeDecl, model))
            return false;

        if (HasBurstCompileOnType(typeDecl, model))
            return false;

        return HasUncoveredSystemLifecycleMethod(typeDecl, model);
    }

    public static bool HasBurstCompile(IReadOnlyList<AttributeInfo> attributes)
    {
        foreach (var attr in attributes)
        {
            var name = attr.Name.Split('.')[^1];
            if (name == "BurstCompile")
                return true;
        }

        return false;
    }

    static bool HasBurstCompileOnType(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (model?.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol symbol)
            return HasBurstCompile(symbol);

        return HasBurstCompile(typeDecl.AttributeLists);
    }

    static bool HasBurstCompile(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var list in attributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                if (GetAttributeSimpleName(attr) == "BurstCompile")
                    return true;
            }
        }

        return false;
    }

    static bool HasBurstCompile(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name is "BurstCompileAttribute" or "BurstCompile")
                return true;
        }

        return false;
    }

    static bool HasUncoveredSystemLifecycleMethod(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (model?.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol symbol)
        {
            var hasLifecycle = false;
            foreach (var member in symbol.GetMembers().OfType<IMethodSymbol>())
            {
                if (!SystemLifecycleMethods.Contains(member.Name))
                    continue;
                hasLifecycle = true;
                if (!MethodHasBurstCompile(member))
                    return true;
            }

            return !hasLifecycle;
        }

        var foundLifecycle = false;
        foreach (var member in typeDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            if (!SystemLifecycleMethods.Contains(member.Identifier.Text))
                continue;
            foundLifecycle = true;
            if (!HasBurstCompile(member.AttributeLists))
                return true;
        }

        return !foundLifecycle;
    }

    static bool MethodHasBurstCompile(IMethodSymbol method)
    {
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.Name is "BurstCompileAttribute" or "BurstCompile")
                return true;
        }

        return false;
    }

    static string GetAttributeSimpleName(AttributeSyntax attribute)
    {
        return attribute.Name switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax qual => qual.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            _ => attribute.Name.ToString().Split('.')[^1]
        };
    }
}
