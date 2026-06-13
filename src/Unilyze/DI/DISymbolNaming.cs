using Unilyze.Pipeline;
namespace Unilyze.DI;

using Microsoft.CodeAnalysis;

// Naming helpers shared by the DI registration resolvers.
// Produces the simple name (matching ITypeSymbol.Name) and a dot-separated
// qualified name that matches TypeIdentity's qualified-name index so the
// pipeline can resolve registration endpoints to TypeIds.
internal static class DISymbolNaming
{
    // Namespace + containing types + simple name, dots, no arity backticks,
    // no type arguments, no global:: prefix. Matches TypeIdentity.CreateQualifiedName.
    static readonly SymbolDisplayFormat QualifiedFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None);

    public static string Qualify(ITypeSymbol symbol) => symbol.ToDisplayString(QualifiedFormat);

    // Strips generic args and trailing nullable/array decorations from a syntactic
    // type reference, matching ITypeSymbol.Name (the simple identifier only).
    public static string SimpleName(string syntacticTypeName)
    {
        var simple = TypeNameFormat.StripGenericArgs(syntacticTypeName);
        var lastDot = simple.LastIndexOf('.');
        return lastDot >= 0 ? simple[(lastDot + 1)..] : simple;
    }

    // Returns a dot-qualified candidate only when the syntactic reference is
    // written fully qualified (contains a namespace/containing-type prefix).
    // Otherwise null, since a bare simple name carries no qualification.
    public static string? QualifiedFromSyntax(string syntacticTypeName)
    {
        var normalized = TypeNameFormat.StripGenericArgs(syntacticTypeName);
        return normalized.Contains('.') ? normalized : null;
    }
}
