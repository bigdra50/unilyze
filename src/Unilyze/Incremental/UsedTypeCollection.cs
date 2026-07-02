using Unilyze.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Incremental;

// The UsedTypes(T) sink: dedups recorded in-source TypeIds and owns the symbol → TypeId mapping
// rule (design doc §4.1). Collectors (declaration / operation / using-target) funnel every
// resolved type through Add; metadata symbols are dropped here so no collector needs to care.
internal sealed class UsedTypeCollection
{
    readonly IReadOnlyDictionary<string, string> _assemblyByFilePath;
    readonly HashSet<string> _used = new(StringComparer.Ordinal);

    public UsedTypeCollection(IReadOnlyDictionary<string, string> assemblyByFilePath) =>
        _assemblyByFilePath = assemblyByFilePath;

    // Unwraps arrays/pointers/generic type arguments to record every named type reachable from
    // `type` (mirrors CboCalculator.CollectNamedTypes).
    public void Add(ITypeSymbol? type)
    {
        switch (type)
        {
            case INamedTypeSymbol named:
                if (TryResolveTypeId(named, _assemblyByFilePath) is { } typeId)
                    _used.Add(typeId);
                foreach (var arg in named.TypeArguments)
                    Add(arg);
                break;
            case IArrayTypeSymbol array:
                Add(array.ElementType);
                break;
            case IPointerTypeSymbol pointer:
                Add(pointer.PointedAtType);
                break;
        }
    }

    // The containing type of a bound member (invocation target, referenced member, constructor,
    // operator method, pattern member) — the single most common thing collectors record.
    public void AddContaining(ISymbol? member) => Add(member?.ContainingType);

    public IReadOnlyList<string> ToSortedList() =>
        _used.OrderBy(id => id, StringComparer.Ordinal).ToList();

    // Symbol → TypeId mapping (§4.1): a symbol with no in-source declaration is a metadata
    // reference — ignored, since it cannot change mid-session (reference-set/TFM changes already
    // flip the global fingerprint → full rebuild). Partial types: every declaring reference
    // resolves to the same TypeId because TypeIdentity.CreateTypeId is name/arity/namespace/
    // assembly-derived, not file-position-derived, so the first resolvable fragment suffices.
    internal static string? TryResolveTypeId(
        INamedTypeSymbol symbol,
        IReadOnlyDictionary<string, string> assemblyByFilePath)
    {
        var definition = symbol.OriginalDefinition;
        foreach (var declRef in definition.DeclaringSyntaxReferences)
        {
            var tree = declRef.SyntaxTree;
            if (string.IsNullOrEmpty(tree.FilePath))
                continue;
            if (!assemblyByFilePath.TryGetValue(Path.GetFullPath(tree.FilePath), out var assembly))
                continue;

            var typeId = declRef.GetSyntax() switch
            {
                TypeDeclarationSyntax td => TypeIdentity.CreateTypeId(td, assembly),
                EnumDeclarationSyntax ed => TypeIdentity.CreateTypeId(ed, assembly),
                DelegateDeclarationSyntax dd => TypeIdentity.CreateTypeId(dd, assembly),
                _ => null
            };
            if (typeId is not null)
                return typeId;
        }

        return null;
    }
}
