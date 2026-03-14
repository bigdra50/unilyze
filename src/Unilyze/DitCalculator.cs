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
        {
            // GetDeclaredSymbol failed: try resolving base type's TypeKind directly
            if (typeDecl.BaseList is { Types.Count: > 0 } baseList
                && model.GetTypeInfo(baseList.Types[0].Type).Type is INamedTypeSymbol baseSymbol)
                return baseSymbol.TypeKind == TypeKind.Interface ? 0 : 1;

            return CalculateSyntactic(typeDecl);
        }

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
        if (typeDecl is InterfaceDeclarationSyntax or StructDeclarationSyntax
            or RecordDeclarationSyntax { ClassOrStructKeyword.Text: "struct" })
            return 0;

        if (typeDecl.BaseList is null)
            return 0;

        var firstBase = typeDecl.BaseList.Types.FirstOrDefault();
        if (firstBase is null)
            return 0;

        // QualifiedNameSyntax: external type reference, skip interface check to avoid namespace collision
        if (firstBase.Type is QualifiedNameSyntax)
            return 1;

        var name = firstBase.Type switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            _ => firstBase.Type.ToString()
        };

        // Check if the base type is declared as an interface in the same syntax tree
        var root = typeDecl.SyntaxTree.GetRoot();
        if (root.DescendantNodes().OfType<InterfaceDeclarationSyntax>()
            .Any(iface => iface.Identifier.Text == name))
            return 0;

        return 1;
    }
}
