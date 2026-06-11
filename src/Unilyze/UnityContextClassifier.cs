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

    public static TypeRole ClassifyRole(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (IsEditorExtension(typeDecl, model))
            return TypeRole.EditorExtension;
        if (IsMonoBehaviour(typeDecl, model))
            return TypeRole.MonoBehaviour;
        if (IsScriptableObject(typeDecl, model))
            return TypeRole.ScriptableObject;
        return TypeRole.PlainCSharp;
    }

    public static TypeRole ClassifyRole(TypeNodeInfo typeInfo, TypeDeclarationSyntax? typeDecl, SemanticModel? model)
    {
        if (typeDecl is not null)
            return ClassifyRole(typeDecl, model);

        if (HasCustomEditorAttribute(typeInfo.Attributes))
            return TypeRole.EditorExtension;
        if (IsDirectBaseName(typeInfo.BaseType, "MonoBehaviour"))
            return TypeRole.MonoBehaviour;
        if (IsDirectBaseName(typeInfo.BaseType, "ScriptableObject"))
            return TypeRole.ScriptableObject;
        return TypeRole.PlainCSharp;
    }

    static bool IsEditorExtension(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (HasCustomEditorAttribute(typeDecl.AttributeLists))
            return true;

        if (model is not null && model.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol symbol)
            return IsEditorExtensionSymbol(symbol);

        return IsDirectEditorBase(typeDecl);
    }

    static bool IsEditorExtensionSymbol(INamedTypeSymbol symbol)
    {
        var current = symbol.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            if (IsEditorType(current))
                return true;
            current = current.BaseType;
        }

        return false;
    }

    static bool IsEditorType(INamedTypeSymbol type)
    {
        if (type.Name is not ("Editor" or "EditorWindow"))
            return false;

        var ns = type.ContainingNamespace?.ToDisplayString();
        return ns is "UnityEditor" or "global::UnityEditor";
    }

    static bool IsDirectEditorBase(TypeDeclarationSyntax typeDecl)
    {
        if (typeDecl.BaseList is null)
            return false;

        foreach (var baseType in typeDecl.BaseList.Types)
        {
            var segment = GetLastIdentifierSegment(baseType.Type);
            if (segment is "Editor" or "EditorWindow")
                return true;
        }

        return false;
    }

    static bool HasCustomEditorAttribute(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var list in attributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                var name = GetAttributeName(attr);
                if (name is "CustomEditor" or "CustomEditorForRenderPipeline")
                    return true;
            }
        }

        return false;
    }

    static bool HasCustomEditorAttribute(IReadOnlyList<AttributeInfo> attributes)
    {
        foreach (var attr in attributes)
        {
            var name = attr.Name.Split('.')[^1];
            if (name is "CustomEditor" or "CustomEditorForRenderPipeline")
                return true;
        }

        return false;
    }

    static string? GetAttributeName(AttributeSyntax attribute)
    {
        return attribute.Name switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax qual => qual.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            _ => attribute.Name.ToString().Split('.')[^1]
        };
    }

    static bool IsScriptableObject(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (model is not null && model.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol symbol)
            return IsScriptableObjectSymbol(symbol);

        return IsDirectScriptableObjectBase(typeDecl);
    }

    static bool IsScriptableObjectSymbol(INamedTypeSymbol symbol)
    {
        var current = symbol.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            if (IsScriptableObjectType(current))
                return true;
            current = current.BaseType;
        }

        return false;
    }

    static bool IsScriptableObjectType(INamedTypeSymbol type)
    {
        if (type.Name != "ScriptableObject")
            return false;

        var ns = type.ContainingNamespace?.ToDisplayString();
        return ns is "UnityEngine" or "global::UnityEngine";
    }

    static bool IsDirectScriptableObjectBase(TypeDeclarationSyntax typeDecl)
    {
        if (typeDecl.BaseList is null)
            return false;

        foreach (var baseType in typeDecl.BaseList.Types)
        {
            if (GetLastIdentifierSegment(baseType.Type) == "ScriptableObject")
                return true;
        }

        return false;
    }

    static bool IsDirectBaseName(string? baseType, string simpleName)
    {
        if (baseType is null)
            return false;

        var segment = baseType.Split('<')[0].Split('.')[^1];
        return segment == simpleName;
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
