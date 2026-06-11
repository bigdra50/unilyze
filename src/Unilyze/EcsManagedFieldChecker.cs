using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

internal static class EcsManagedFieldChecker
{
    static readonly HashSet<string> SyntaxManagedTypeNames = new(StringComparer.Ordinal)
    {
        "string", "String", "object", "Object", "StringBuilder",
        "List", "Dictionary", "HashSet", "Queue", "Stack", "LinkedList",
        "IEnumerable", "ICollection", "IList", "IDictionary", "IReadOnlyList",
        "IReadOnlyCollection", "IReadOnlyDictionary", "Action", "Func", "Delegate"
    };

    public static bool IsManagedComponentField(FieldDeclarationSyntax field, SemanticModel? model)
    {
        if (model is not null)
        {
            foreach (var variable in field.Declaration.Variables)
            {
                if (model.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
                    continue;
                if (fieldSymbol.Type.IsReferenceType)
                    return true;
            }

            return false;
        }

        return IsLikelyManagedTypeSyntax(field.Declaration.Type);
    }

    static bool IsLikelyManagedTypeSyntax(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            PredefinedTypeSyntax predefined => predefined.Keyword.Text is "string" or "object",
            ArrayTypeSyntax => true,
            NullableTypeSyntax nullable => IsLikelyManagedTypeSyntax(nullable.ElementType),
            GenericNameSyntax generic => SyntaxManagedTypeNames.Contains(generic.Identifier.Text),
            IdentifierNameSyntax id => SyntaxManagedTypeNames.Contains(id.Identifier.Text),
            QualifiedNameSyntax qual => SyntaxManagedTypeNames.Contains(qual.Right.Identifier.Text),
            AliasQualifiedNameSyntax alias => SyntaxManagedTypeNames.Contains(alias.Name.Identifier.Text),
            _ => false
        };
    }
}
