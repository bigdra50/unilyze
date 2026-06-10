using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public sealed record UnityTypeContext(bool IsMonoBehaviour, IReadOnlySet<string> HotPathMethodNames);

public static class UnityContextClassifier
{
    static readonly HashSet<string> AlwaysHotPathMethods = new(StringComparer.Ordinal)
    {
        "Update", "FixedUpdate", "LateUpdate", "OnGUI"
    };

    static readonly IReadOnlySet<string> EmptyHotPath = new HashSet<string>();

    public static UnityTypeContext Classify(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (!IsMonoBehaviour(typeDecl, model))
            return new UnityTypeContext(false, EmptyHotPath);

        return new UnityTypeContext(true, CollectHotPathMethodNames(typeDecl, model));
    }

    static bool IsMonoBehaviour(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (model is not null && model.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol symbol)
            return IsMonoBehaviourSymbol(symbol);

        return IsDirectMonoBehaviourBase(typeDecl);
    }

    static bool IsMonoBehaviourSymbol(INamedTypeSymbol symbol)
    {
        var current = symbol.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            if (IsMonoBehaviourType(current))
                return true;
            current = current.BaseType;
        }

        return false;
    }

    static bool IsMonoBehaviourType(INamedTypeSymbol type)
    {
        if (type.Name != "MonoBehaviour")
            return false;

        var ns = type.ContainingNamespace?.ToDisplayString();
        if (ns is "UnityEngine" or "global::UnityEngine")
            return true;

        return type.ToDisplayString() is "UnityEngine.MonoBehaviour" or "global::UnityEngine.MonoBehaviour";
    }

    static bool IsDirectMonoBehaviourBase(TypeDeclarationSyntax typeDecl)
    {
        if (typeDecl.BaseList is null)
            return false;

        foreach (var baseType in typeDecl.BaseList.Types)
        {
            if (GetLastIdentifierSegment(baseType.Type) == "MonoBehaviour")
                return true;
        }

        return false;
    }

    static IReadOnlySet<string> CollectHotPathMethodNames(
        TypeDeclarationSyntax typeDecl,
        SemanticModel? model)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in typeDecl.Members)
        {
            if (member is not MethodDeclarationSyntax method)
                continue;

            var name = method.Identifier.Text;
            if (AlwaysHotPathMethods.Contains(name) || IsCoroutineMethod(method, model))
                names.Add(name);
        }

        return names;
    }

    static bool IsCoroutineMethod(MethodDeclarationSyntax method, SemanticModel? model)
    {
        if (GetLastIdentifierSegment(method.ReturnType) == "IEnumerator")
            return true;

        if (model is null)
            return false;

        var returnType = model.GetTypeInfo(method.ReturnType).Type;
        return returnType is not null && IsIEnumerator(returnType);
    }

    static bool IsIEnumerator(ITypeSymbol returnType)
    {
        if (returnType.SpecialType == SpecialType.System_Collections_IEnumerator)
            return true;

        if (returnType.Name != "IEnumerator")
            return false;

        var ns = returnType.ContainingNamespace?.ToDisplayString();
        return ns is "System.Collections" or "global::System.Collections";
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
