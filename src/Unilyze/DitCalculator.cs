using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public static class DitCalculator
{
    public static int Calculate(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        return model is not null
            ? CalculateSemantic(typeDecl, model)
            : CalculateSyntactic(typeDecl);
    }

    static int CalculateSemantic(TypeDeclarationSyntax typeDecl, SemanticModel model)
    {
        if (typeDecl is InterfaceDeclarationSyntax)
            return 0;

        var symbol = model.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        if (symbol is null)
            return CalculateSyntactic(typeDecl);

        if (symbol.TypeKind is TypeKind.Struct)
            return 0;

        var depth = 0;
        var current = symbol.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            depth++;
            current = current.BaseType;
        }

        return depth;
    }

    static int CalculateSyntactic(TypeDeclarationSyntax typeDecl)
    {
        if (typeDecl is InterfaceDeclarationSyntax or StructDeclarationSyntax)
            return 0;

        if (typeDecl.BaseList is null)
            return 0;

        var firstBase = typeDecl.BaseList.Types.FirstOrDefault();
        if (firstBase is null)
            return 0;

        var name = firstBase.Type switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                GenericNameSyntax generic => generic.Identifier.Text,
                _ => qualified.Right.ToString()
            },
            _ => firstBase.Type.ToString()
        };

        if (name.Length >= 2 && name[0] == 'I' && char.IsUpper(name[1]))
            return 0;

        return 1;
    }
}
