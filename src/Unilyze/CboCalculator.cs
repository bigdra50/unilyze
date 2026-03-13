using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public static class CboCalculator
{
    static readonly HashSet<string> SyntacticPrimitives = new(StringComparer.Ordinal)
    {
        "bool", "byte", "sbyte", "char", "decimal", "double", "float",
        "int", "uint", "long", "ulong", "short", "ushort", "string",
        "object", "void", "nint", "nuint", "dynamic", "var"
    };

    public static int Calculate(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        return model is not null
            ? CalculateSemantic(typeDecl, model)
            : CalculateSyntactic(typeDecl);
    }

    static int CalculateSemantic(TypeDeclarationSyntax typeDecl, SemanticModel model)
    {
        var containingType = model.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        var coupledTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var node in typeDecl.DescendantNodes())
        {
            ITypeSymbol? typeSymbol = node switch
            {
                TypeSyntax typeSyntax => model.GetTypeInfo(typeSyntax).Type,
                ObjectCreationExpressionSyntax creation => model.GetTypeInfo(creation).Type,
                CastExpressionSyntax cast => model.GetTypeInfo(cast).Type,
                _ => null
            };

            if (typeSymbol is not null)
                CollectNamedTypes(typeSymbol, coupledTypes);
        }

        // Remove self and excluded types
        coupledTypes.RemoveWhere(t =>
            (containingType is not null && SymbolEqualityComparer.Default.Equals(t, containingType))
            || IsExcludedType(t));

        return coupledTypes.Count;
    }

    static void CollectNamedTypes(ITypeSymbol type, HashSet<INamedTypeSymbol> result)
    {
        switch (type)
        {
            case INamedTypeSymbol named:
                result.Add(named.OriginalDefinition);
                foreach (var arg in named.TypeArguments)
                    CollectNamedTypes(arg, result);
                break;
            case IArrayTypeSymbol array:
                CollectNamedTypes(array.ElementType, result);
                break;
        }
    }

    static bool IsExcludedType(INamedTypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None)
            return true;

        var fullName = type.OriginalDefinition.ToDisplayString();
        return fullName is "System.ValueType" or "System.Enum"
            or "System.Delegate" or "System.MulticastDelegate"
            or "System.Attribute" or "System.Void";
    }

    static int CalculateSyntactic(TypeDeclarationSyntax typeDecl)
    {
        var typeNames = new HashSet<string>(StringComparer.Ordinal);
        var selfName = typeDecl.Identifier.Text;

        // Base list
        if (typeDecl.BaseList is not null)
        {
            foreach (var baseType in typeDecl.BaseList.Types)
                CollectTypeNames(baseType.Type, typeNames);
        }

        // Field types
        foreach (var field in typeDecl.Members.OfType<FieldDeclarationSyntax>())
            CollectTypeNames(field.Declaration.Type, typeNames);

        // Property types
        foreach (var prop in typeDecl.Members.OfType<PropertyDeclarationSyntax>())
            CollectTypeNames(prop.Type, typeNames);

        // Method signatures and bodies
        foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            CollectTypeNames(method.ReturnType, typeNames);
            foreach (var param in method.ParameterList.Parameters)
            {
                if (param.Type is not null)
                    CollectTypeNames(param.Type, typeNames);
            }
            CollectBodyTypeNames(method.Body, typeNames);
            CollectBodyTypeNames(method.ExpressionBody, typeNames);
        }

        // Constructor parameters and bodies
        foreach (var ctor in typeDecl.Members.OfType<ConstructorDeclarationSyntax>())
        {
            foreach (var param in ctor.ParameterList.Parameters)
            {
                if (param.Type is not null)
                    CollectTypeNames(param.Type, typeNames);
            }
            CollectBodyTypeNames(ctor.Body, typeNames);
            CollectBodyTypeNames(ctor.ExpressionBody, typeNames);
        }

        typeNames.Remove(selfName);
        typeNames.ExceptWith(SyntacticPrimitives);

        return typeNames.Count;
    }

    static void CollectTypeNames(TypeSyntax? typeSyntax, HashSet<string> typeNames)
    {
        switch (typeSyntax)
        {
            case IdentifierNameSyntax id:
                typeNames.Add(id.Identifier.Text);
                break;
            case GenericNameSyntax generic:
                typeNames.Add(generic.Identifier.Text);
                foreach (var arg in generic.TypeArgumentList.Arguments)
                    CollectTypeNames(arg, typeNames);
                break;
            case QualifiedNameSyntax qualified:
                CollectTypeNames(qualified.Right, typeNames);
                break;
            case ArrayTypeSyntax array:
                CollectTypeNames(array.ElementType, typeNames);
                break;
            case NullableTypeSyntax nullable:
                CollectTypeNames(nullable.ElementType, typeNames);
                break;
            case TupleTypeSyntax tuple:
                foreach (var element in tuple.Elements)
                    CollectTypeNames(element.Type, typeNames);
                break;
        }
    }

    static void CollectBodyTypeNames(SyntaxNode? body, HashSet<string> typeNames)
    {
        if (body is null) return;

        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case LocalDeclarationStatementSyntax local:
                    CollectTypeNames(local.Declaration.Type, typeNames);
                    break;
                case ObjectCreationExpressionSyntax creation:
                    CollectTypeNames(creation.Type, typeNames);
                    break;
                case CastExpressionSyntax cast:
                    CollectTypeNames(cast.Type, typeNames);
                    break;
                case TypeOfExpressionSyntax typeOf:
                    CollectTypeNames(typeOf.Type, typeNames);
                    break;
            }
        }
    }
}
