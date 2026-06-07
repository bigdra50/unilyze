using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

// Lookup tables from syntax trees, shared by BaseTypeResolver and SemanticEnricher.
internal static class SyntaxLookups
{
    internal static Dictionary<string, SyntaxTree> BuildTreeLookup(
        CompilationResult compilationResult,
        IReadOnlyList<SyntaxTree> syntaxTrees)
    {
        var treeByPath = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
        var sourceSet = compilationResult.Compilation?.SyntaxTrees ?? syntaxTrees;
        foreach (var tree in sourceSet)
        {
            if (!string.IsNullOrEmpty(tree.FilePath))
                treeByPath.TryAdd(Path.GetFullPath(tree.FilePath), tree);
        }
        return treeByPath;
    }

    internal static Dictionary<string, TypeDeclarationSyntax> BuildTypeDeclLookup(
        IReadOnlyList<TypeNodeInfo> allTypes,
        Dictionary<string, SyntaxTree> treeByPath)
    {
        var typeDeclLookup = new Dictionary<string, TypeDeclarationSyntax>();
        foreach (var type in allTypes)
        {
            if (type.Kind is "enum" or "delegate") continue;
            if (!treeByPath.TryGetValue(Path.GetFullPath(type.FilePath), out var tree)) continue;

            var typeDecl = FindTypeDeclaration(tree, type);
            if (typeDecl is not null)
                typeDeclLookup.TryAdd(TypeIdentity.GetTypeId(type), typeDecl);
        }
        return typeDeclLookup;
    }

    internal static TypeDeclarationSyntax? FindTypeDeclaration(SyntaxTree tree, TypeNodeInfo type) =>
        tree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(td => TypeIdentity.CreateTypeId(td, type.Assembly) == TypeIdentity.GetTypeId(type));
}
