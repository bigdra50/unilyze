using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Incremental;

// Records, for a re-enriched type T, the set of in-source TypeIds that T's enrichment actually
// resolved (design doc §4.1: UsedTypes(T)), inverted into RDeps(B) = {T | B ∈ UsedTypes(T)} for
// precise invalidation. Orchestrates the three collection surfaces:
//   - declaration side (base chain / interfaces / member signatures / attributes / constraints)
//     — DeclarationUsageCollector;
//   - the file's resolution environment (using-static / alias targets) — recorded here, since it
//     is a per-file rule that applies to every type declared in the file (§4.1 point 4; plain
//     `using N;` namespace imports are out of scope);
//   - member-body operations (one IOperation walk) — OperationUsageCollector.
// Symbol → TypeId mapping and metadata filtering live in UsedTypeCollection.
internal static class UsageRecorder
{
    public static IReadOnlyList<string> Record(
        TypeDeclarationSyntax typeDecl,
        SemanticModel model,
        IReadOnlyDictionary<string, string> assemblyByFilePath)
    {
        var used = new UsedTypeCollection(assemblyByFilePath);

        if (model.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol selfSymbol)
            DeclarationUsageCollector.Collect(selfSymbol, used);

        CollectFileUsingTargets(typeDecl.SyntaxTree, model, used);
        OperationUsageCollector.Collect(typeDecl, model, used);

        return used.ToSortedList();
    }

    static void CollectFileUsingTargets(SyntaxTree tree, SemanticModel model, UsedTypeCollection used)
    {
        if (tree.GetRoot() is not CompilationUnitSyntax root)
            return;

        foreach (var directive in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (directive.Alias is not null)
            {
                if (model.GetDeclaredSymbol(directive) is IAliasSymbol { Target: INamedTypeSymbol aliasTarget })
                    used.Add(aliasTarget);
                continue;
            }

            if (directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) && directive.Name is { } name
                && model.GetSymbolInfo(name).Symbol is INamedTypeSymbol staticTarget)
                used.Add(staticTarget);
        }
    }

    // Stable entry point for callers/tests; the mapping rule itself lives with the sink.
    internal static string? TryResolveTypeId(
        INamedTypeSymbol symbol,
        IReadOnlyDictionary<string, string> assemblyByFilePath) =>
        UsedTypeCollection.TryResolveTypeId(symbol, assemblyByFilePath);
}
