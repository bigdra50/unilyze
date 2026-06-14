using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Pipeline;

internal static class MemberBodyEnumerator
{
    internal readonly record struct ExecutableMember(SyntaxNode ScanRoot, string MemberName);

    internal static IEnumerable<ExecutableMember> Enumerate(TypeDeclarationSyntax typeDecl)
    {
        foreach (var member in typeDecl.Members)
        {
            foreach (var executable in EnumerateMember(member))
                yield return executable;
        }
    }

    static IEnumerable<ExecutableMember> EnumerateMember(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method => EnumerateMethod(method),
        ConstructorDeclarationSyntax ctor => EnumerateConstructor(ctor),
        PropertyDeclarationSyntax prop => EnumerateProperty(prop),
        IndexerDeclarationSyntax indexer => EnumerateIndexer(indexer),
        OperatorDeclarationSyntax op => EnumerateOperator(op),
        ConversionOperatorDeclarationSyntax conv => EnumerateConversionOperator(conv),
        FieldDeclarationSyntax field => EnumerateField(field),
        _ => []
    };

    static IEnumerable<ExecutableMember> EnumerateMethod(MethodDeclarationSyntax method)
    {
        var name = method.Identifier.Text;
        yield return new(method, name);
        foreach (var local in EnumerateNestedLocalFunctions(method, name))
            yield return local;
    }

    static IEnumerable<ExecutableMember> EnumerateConstructor(ConstructorDeclarationSyntax ctor)
    {
        var name = ctor.Identifier.Text + ".ctor";
        yield return new(ctor, name);
        foreach (var local in EnumerateNestedLocalFunctions(ctor, name))
            yield return local;
    }

    static IEnumerable<ExecutableMember> EnumerateOperator(OperatorDeclarationSyntax op)
    {
        var name = GetOperatorMemberName(op);
        yield return new(op, name);
        foreach (var local in EnumerateNestedLocalFunctions(op, name))
            yield return local;
    }

    static IEnumerable<ExecutableMember> EnumerateConversionOperator(ConversionOperatorDeclarationSyntax conv)
    {
        var name = conv.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ImplicitKeyword)
            ? "op_Implicit"
            : "op_Explicit";
        yield return new(conv, name);
        foreach (var local in EnumerateNestedLocalFunctions(conv, name))
            yield return local;
    }

    static IEnumerable<ExecutableMember> EnumerateField(FieldDeclarationSyntax field)
    {
        foreach (var variable in field.Declaration.Variables)
        {
            if (variable.Initializer?.Value is not { } initExpr)
                continue;

            var initName = variable.Identifier.Text + ".init";
            yield return new(initExpr, initName);
            foreach (var local in EnumerateNestedLocalFunctions(initExpr, initName))
                yield return local;
        }
    }

    static IEnumerable<ExecutableMember> EnumerateProperty(PropertyDeclarationSyntax prop)
    {
        if (prop.ExpressionBody is { } exprBody)
        {
            var getterName = "get_" + prop.Identifier.Text;
            yield return new(exprBody, getterName);
            foreach (var local in EnumerateNestedLocalFunctions(exprBody, getterName))
                yield return local;
        }

        if (prop.Initializer?.Value is { } propInit)
        {
            var initName = prop.Identifier.Text + ".init";
            yield return new(propInit, initName);
            foreach (var local in EnumerateNestedLocalFunctions(propInit, initName))
                yield return local;
        }

        if (prop.AccessorList is null)
            yield break;

        foreach (var accessor in prop.AccessorList.Accessors)
        {
            if (!HasExecutableBody(accessor))
                continue;

            var accessorName = GetPropertyAccessorMemberName(prop.Identifier.Text, accessor);
            yield return new(accessor, accessorName);
            foreach (var local in EnumerateNestedLocalFunctions(accessor, accessorName))
                yield return local;
        }
    }

    static IEnumerable<ExecutableMember> EnumerateIndexer(IndexerDeclarationSyntax indexer)
    {
        if (indexer.AccessorList is null)
            yield break;

        foreach (var accessor in indexer.AccessorList.Accessors)
        {
            if (!HasExecutableBody(accessor))
                continue;

            var accessorName = GetIndexerAccessorMemberName(accessor);
            yield return new(accessor, accessorName);
            foreach (var local in EnumerateNestedLocalFunctions(accessor, accessorName))
                yield return local;
        }
    }

    static bool HasExecutableBody(AccessorDeclarationSyntax accessor) =>
        accessor.Body is not null || accessor.ExpressionBody is not null;

    static string GetPropertyAccessorMemberName(string propertyName, AccessorDeclarationSyntax accessor) =>
        GetAccessorPrefix(accessor) + propertyName;

    static string GetIndexerAccessorMemberName(AccessorDeclarationSyntax accessor) =>
        "this_" + GetAccessorSuffix(accessor);

    static string GetAccessorPrefix(AccessorDeclarationSyntax accessor) =>
        GetAccessorSuffix(accessor) + "_";

    static string GetAccessorSuffix(AccessorDeclarationSyntax accessor) => accessor.Kind() switch
    {
        SyntaxKind.GetAccessorDeclaration => "get",
        SyntaxKind.SetAccessorDeclaration => "set",
        SyntaxKind.InitAccessorDeclaration => "init",
        _ => "accessor"
    };

    internal static string GetOperatorMemberName(OperatorDeclarationSyntax op)
    {
        var isUnary = op.ParameterList.Parameters.Count == 1;
        var opKind = op.OperatorToken.Kind();
        if (TryGetOperatorName(isUnary, opKind, out var name))
            return name;

        return "op_" + op.OperatorToken.Text;
    }

    static bool TryGetOperatorName(bool isUnary, SyntaxKind opKind, out string name)
    {
        if (isUnary)
            return UnaryOperatorNames.TryGetValue(opKind, out name);

        return BinaryOperatorNames.TryGetValue(opKind, out name);
    }

    static readonly Dictionary<SyntaxKind, string> UnaryOperatorNames = new()
    {
        [SyntaxKind.PlusToken] = "op_UnaryPlus",
        [SyntaxKind.MinusToken] = "op_UnaryNegation",
        [SyntaxKind.ExclamationToken] = "op_LogicalNot",
        [SyntaxKind.TildeToken] = "op_OnesComplement",
        [SyntaxKind.PlusPlusToken] = "op_Increment",
        [SyntaxKind.MinusMinusToken] = "op_Decrement",
        [SyntaxKind.TrueKeyword] = "op_True",
        [SyntaxKind.FalseKeyword] = "op_False",
    };

    static readonly Dictionary<SyntaxKind, string> BinaryOperatorNames = new()
    {
        [SyntaxKind.PlusToken] = "op_Addition",
        [SyntaxKind.MinusToken] = "op_Subtraction",
        [SyntaxKind.AsteriskToken] = "op_Multiply",
        [SyntaxKind.SlashToken] = "op_Division",
        [SyntaxKind.PercentToken] = "op_Modulus",
        [SyntaxKind.AmpersandToken] = "op_BitwiseAnd",
        [SyntaxKind.BarToken] = "op_BitwiseOr",
        [SyntaxKind.CaretToken] = "op_ExclusiveOr",
        [SyntaxKind.LessThanLessThanToken] = "op_LeftShift",
        [SyntaxKind.GreaterThanGreaterThanToken] = "op_RightShift",
        [SyntaxKind.ExclamationEqualsToken] = "op_Inequality",
        [SyntaxKind.EqualsEqualsToken] = "op_Equality",
        [SyntaxKind.GreaterThanToken] = "op_GreaterThan",
        [SyntaxKind.LessThanToken] = "op_LessThan",
        [SyntaxKind.GreaterThanEqualsToken] = "op_GreaterThanOrEqual",
        [SyntaxKind.LessThanEqualsToken] = "op_LessThanOrEqual",
    };

    internal static bool IsInsideNestedLocalFunction(SyntaxNode node, SyntaxNode scanRoot)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor == scanRoot)
                break;
            if (ancestor is LocalFunctionStatementSyntax)
                return true;
        }

        return false;
    }

    internal static IEnumerable<T> DescendantNodesExcludingLocalFunctions<T>(SyntaxNode scanRoot)
        where T : SyntaxNode
    {
        foreach (var node in scanRoot.DescendantNodesAndSelf())
        {
            if (node is T typed && !IsInsideNestedLocalFunction(node, scanRoot))
                yield return typed;
        }
    }

    static IEnumerable<ExecutableMember> EnumerateNestedLocalFunctions(SyntaxNode parent, string parentMemberName)
    {
        foreach (var node in parent.DescendantNodes())
        {
            if (node is not LocalFunctionStatementSyntax localFunc)
                continue;

            if (HasEnclosingLocalFunction(localFunc, parent))
                continue;

            var name = parentMemberName + "." + localFunc.Identifier.Text;
            yield return new(localFunc, name);
            foreach (var nested in EnumerateNestedLocalFunctions(localFunc, name))
                yield return nested;
        }
    }

    static bool HasEnclosingLocalFunction(LocalFunctionStatementSyntax localFunc, SyntaxNode parent)
    {
        foreach (var ancestor in localFunc.Ancestors())
        {
            if (ancestor == parent)
                break;
            if (ancestor is LocalFunctionStatementSyntax)
                return true;
        }

        return false;
    }
}
