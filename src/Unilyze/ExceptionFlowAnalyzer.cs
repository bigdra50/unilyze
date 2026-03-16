namespace Unilyze;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public sealed record CatchAllClause(string MethodName, int Line, bool HasRethrow);
public sealed record MissingInnerException(string MethodName, string NewExceptionType, int Line);
public sealed record SystemExceptionThrow(string MethodName, int Line);

public sealed record ExceptionFlowResult(
    IReadOnlyList<CatchAllClause> CatchAllClauses,
    IReadOnlyList<MissingInnerException> MissingInnerExceptions,
    IReadOnlyList<SystemExceptionThrow> SystemExceptionThrows);

public static class ExceptionFlowAnalyzer
{
    public static ExceptionFlowResult Analyze(
        TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        var catchAllClauses = new List<CatchAllClause>();
        var missingInnerExceptions = new List<MissingInnerException>();
        var systemExceptionThrows = new List<SystemExceptionThrow>();

        foreach (var catchClause in typeDecl.DescendantNodes().OfType<CatchClauseSyntax>())
        {
            var methodName = GetEnclosingMethodName(catchClause);
            var line = catchClause.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

            if (IsCatchAll(catchClause, model))
            {
                var hasRethrow = ContainsRethrow(catchClause.Block);
                catchAllClauses.Add(new CatchAllClause(methodName, line, hasRethrow));
            }

            DetectMissingInnerException(catchClause, methodName, missingInnerExceptions);
        }

        foreach (var throwStmt in typeDecl.DescendantNodes().OfType<ThrowStatementSyntax>())
        {
            DetectSystemExceptionThrow(throwStmt, model, systemExceptionThrows);
        }

        return new ExceptionFlowResult(catchAllClauses, missingInnerExceptions, systemExceptionThrows);
    }

    static string GetEnclosingMethodName(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case MethodDeclarationSyntax method:
                    return method.Identifier.Text;
                case ConstructorDeclarationSyntax ctor:
                    return ctor.Identifier.Text;
                case PropertyDeclarationSyntax prop:
                    return prop.Identifier.Text;
            }
        }

        return "<unknown>";
    }

    static bool IsCatchAll(CatchClauseSyntax catchClause, SemanticModel? model)
    {
        var declaration = catchClause.Declaration;

        // bare catch: `catch { }`
        if (declaration is null)
            return true;

        var typeSyntax = declaration.Type;

        if (model is not null)
        {
            var typeInfo = model.GetTypeInfo(typeSyntax);
            if (typeInfo.Type is INamedTypeSymbol namedType)
            {
                return namedType.ToDisplayString() == "System.Exception";
            }
        }

        // Syntactic fallback
        var typeName = typeSyntax.ToString();
        return typeName is "Exception" or "System.Exception";
    }

    static bool ContainsRethrow(BlockSyntax block)
    {
        return block.DescendantNodes()
            .OfType<ThrowStatementSyntax>()
            .Any(t => t.Expression is null); // `throw;` has null Expression
    }

    static void DetectMissingInnerException(
        CatchClauseSyntax catchClause,
        string methodName,
        List<MissingInnerException> results)
    {
        var declaration = catchClause.Declaration;
        if (declaration is null)
            return;

        var catchVariableName = declaration.Identifier.Text;
        if (string.IsNullOrEmpty(catchVariableName))
            return;

        foreach (var throwStmt in catchClause.Block.DescendantNodes().OfType<ThrowStatementSyntax>())
        {
            // Skip rethrow: `throw;`
            if (throwStmt.Expression is null)
                continue;

            if (throwStmt.Expression is not ObjectCreationExpressionSyntax creation)
                continue;

            var args = creation.ArgumentList;
            if (args is null)
            {
                var line = throwStmt.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var exType = creation.Type.ToString();
                results.Add(new MissingInnerException(methodName, exType, line));
                continue;
            }

            var referencesCatchVar = args.Arguments
                .SelectMany(a => a.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                .Any(id => id.Identifier.Text == catchVariableName);

            if (!referencesCatchVar)
            {
                var line = throwStmt.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var exType = creation.Type.ToString();
                results.Add(new MissingInnerException(methodName, exType, line));
            }
        }
    }

    static void DetectSystemExceptionThrow(
        ThrowStatementSyntax throwStmt,
        SemanticModel? model,
        List<SystemExceptionThrow> results)
    {
        // Skip rethrow
        if (throwStmt.Expression is null)
            return;

        if (throwStmt.Expression is not ObjectCreationExpressionSyntax creation)
            return;

        if (IsSystemException(creation, model))
        {
            var methodName = GetEnclosingMethodName(throwStmt);
            var line = throwStmt.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            results.Add(new SystemExceptionThrow(methodName, line));
        }
    }

    static bool IsSystemException(ObjectCreationExpressionSyntax creation, SemanticModel? model)
    {
        if (model is not null)
        {
            var typeInfo = model.GetTypeInfo(creation);
            if (typeInfo.Type is INamedTypeSymbol namedType)
            {
                // Exact match: System.Exception itself, not subclasses
                return namedType.ToDisplayString() == "System.Exception";
            }
        }

        // Syntactic fallback
        var typeName = creation.Type.ToString();
        return typeName is "Exception" or "System.Exception";
    }
}
