using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

// Resolves base types and interfaces semantically (e.g. distinguishing interfaces
// from base classes in the base list) before metrics enrichment.
internal static class BaseTypeResolver
{
    internal static IReadOnlyList<TypeNodeInfo> ResolveTypeRelationships(
        IReadOnlyList<TypeNodeInfo> allTypes,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        CompilationResult compilationResult)
    {
        if (compilationResult.Compilation is null)
            return allTypes;

        var treeByPath = SyntaxLookups.BuildTreeLookup(compilationResult, syntaxTrees);
        var typeDeclLookup = SyntaxLookups.BuildTypeDeclLookup(allTypes, treeByPath);
        var modelCache = new ConcurrentDictionary<SyntaxTree, SemanticModel>();
        var resolved = new List<TypeNodeInfo>(allTypes.Count);

        foreach (var type in allTypes)
        {
            if (type.Kind is "enum" or "delegate")
            {
                resolved.Add(type);
                continue;
            }

            if (!typeDeclLookup.TryGetValue(TypeIdentity.GetTypeId(type), out var typeDecl))
            {
                resolved.Add(type);
                continue;
            }

            var model = modelCache.GetOrAdd(typeDecl.SyntaxTree, t => compilationResult.Compilation.GetSemanticModel(t));
            resolved.Add(ResolveExplicitBaseList(type, typeDecl, model));
        }

        return resolved;
    }

    static TypeNodeInfo ResolveExplicitBaseList(
        TypeNodeInfo type,
        TypeDeclarationSyntax typeDecl,
        SemanticModel model)
    {
        if (typeDecl.BaseList is null)
            return type;

        string? baseType = null;
        var interfaces = new List<string>();

        foreach (var baseTypeSyntax in typeDecl.BaseList.Types)
        {
            var typeSymbol = model.GetTypeInfo(baseTypeSyntax.Type).Type as INamedTypeSymbol;
            var displayName = typeSymbol?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                ?? baseTypeSyntax.Type.ToString();

            if (type.Kind == "interface" || typeSymbol?.TypeKind == TypeKind.Interface)
            {
                interfaces.Add(displayName);
                continue;
            }

            baseType ??= displayName;
        }

        return type with
        {
            BaseType = type.Kind == "interface" ? null : baseType,
            Interfaces = interfaces.Distinct().ToList()
        };
    }
}
