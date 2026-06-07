using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

internal static class TypeIdentity
{
    public static string CreateTypeId(TypeDeclarationSyntax declaration, string assemblyName)
        => CreateTypeIdCore(
            declaration,
            assemblyName,
            declaration.Identifier.Text,
            declaration.TypeParameterList?.Parameters.Count ?? 0);

    public static string CreateTypeId(EnumDeclarationSyntax declaration, string assemblyName)
        => CreateTypeIdCore(declaration, assemblyName, declaration.Identifier.Text, 0);

    public static string CreateTypeId(DelegateDeclarationSyntax declaration, string assemblyName)
        => CreateTypeIdCore(
            declaration,
            assemblyName,
            declaration.Identifier.Text,
            declaration.TypeParameterList?.Parameters.Count ?? 0);

    public static string CreateQualifiedName(TypeDeclarationSyntax declaration)
        => CreateQualifiedNameCore(declaration, declaration.Identifier.Text);

    public static string CreateQualifiedName(EnumDeclarationSyntax declaration)
        => CreateQualifiedNameCore(declaration, declaration.Identifier.Text);

    public static string CreateQualifiedName(DelegateDeclarationSyntax declaration)
        => CreateQualifiedNameCore(declaration, declaration.Identifier.Text);

    public static string GetTypeId(TypeNodeInfo type)
        => string.IsNullOrWhiteSpace(type.TypeId)
            ? BuildFallbackTypeId(type.Assembly, type.Namespace, type.Name)
            : type.TypeId;

    public static string GetTypeId(TypeMetrics metrics)
        => string.IsNullOrWhiteSpace(metrics.TypeId)
            ? BuildFallbackTypeId(metrics.Assembly, metrics.Namespace, metrics.TypeName)
            : metrics.TypeId;

    public static string GetQualifiedName(TypeNodeInfo type)
        => string.IsNullOrWhiteSpace(type.QualifiedName)
            ? BuildFallbackQualifiedName(type.Namespace, type.Name)
            : type.QualifiedName;

    public static string GetQualifiedName(TypeMetrics metrics)
        => string.IsNullOrWhiteSpace(metrics.QualifiedName)
            ? BuildFallbackQualifiedName(metrics.Namespace, metrics.TypeName)
            : metrics.QualifiedName;

    public static string GetSimpleName(TypeNodeInfo type) => TypeNameFormat.StripGenericArgs(type.Name);

    public static IReadOnlyList<string> GetContainingQualifiedNamePrefixes(TypeNodeInfo type)
    {
        var qualifiedName = GetQualifiedName(type);
        if (string.IsNullOrWhiteSpace(qualifiedName))
            return [];

        var namespacePrefix = string.IsNullOrEmpty(type.Namespace) ? "" : type.Namespace + ".";
        var relativeName = namespacePrefix.Length > 0 && qualifiedName.StartsWith(namespacePrefix, StringComparison.Ordinal)
            ? qualifiedName[namespacePrefix.Length..]
            : qualifiedName;

        var segments = relativeName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 1)
            return [];

        var result = new List<string>(segments.Length - 1);
        for (var i = segments.Length - 1; i >= 1; i--)
        {
            var prefix = string.Join('.', segments[..i]);
            result.Add(string.IsNullOrEmpty(type.Namespace) ? prefix : $"{type.Namespace}.{prefix}");
        }

        return result;
    }

    static string CreateTypeIdCore(MemberDeclarationSyntax declaration, string assemblyName, string name, int arity)
    {
        var namespaceName = GetNamespace(declaration);
        var segments = declaration.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .Reverse()
            .Select(GetTypeSegment)
            .ToList();
        segments.Add((name, arity));

        var path = string.Join("+", segments.Select(FormatTypeSegment));
        return string.IsNullOrEmpty(namespaceName)
            ? $"{assemblyName}::{path}"
            : $"{assemblyName}::{namespaceName}.{path}";
    }

    static string CreateQualifiedNameCore(MemberDeclarationSyntax declaration, string name)
    {
        var namespaceName = GetNamespace(declaration);
        var segments = declaration.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .Reverse()
            .Select(t => t.Identifier.Text)
            .Append(name)
            .ToArray();

        var path = string.Join('.', segments);
        return string.IsNullOrEmpty(namespaceName) ? path : $"{namespaceName}.{path}";
    }

    static (string Name, int Arity) GetTypeSegment(TypeDeclarationSyntax declaration)
        => (declaration.Identifier.Text, declaration.TypeParameterList?.Parameters.Count ?? 0);

    static string FormatTypeSegment((string Name, int Arity) segment)
        => segment.Arity > 0 ? $"{segment.Name}`{segment.Arity}" : segment.Name;

    static string GetNamespace(SyntaxNode node)
        => node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "";

    static string BuildFallbackTypeId(string assembly, string ns, string name)
    {
        var simpleName = TypeNameFormat.StripGenericArgs(name);
        var arity = TypeNameFormat.CountGenericArity(name);
        var segment = arity > 0 ? $"{simpleName}`{arity}" : simpleName;
        return string.IsNullOrEmpty(ns)
            ? $"{assembly}::{segment}"
            : $"{assembly}::{ns}.{segment}";
    }

    static string BuildFallbackQualifiedName(string ns, string name)
    {
        var simpleName = TypeNameFormat.StripGenericArgs(name);
        return string.IsNullOrEmpty(ns) ? simpleName : $"{ns}.{simpleName}";
    }
}
