using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public sealed record ClosureCapture(
    string MethodName, string LambdaDescription,
    IReadOnlyList<string> CapturedVariables, int Line);

public static class ClosureDetector
{
    public static IReadOnlyList<ClosureCapture> Detect(
        TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        var results = new List<ClosureCapture>();

        foreach (var (scanRoot, memberName) in MemberBodyEnumerator.Enumerate(typeDecl))
            DetectInMember(scanRoot, memberName, model, results);

        return results;
    }

    static void DetectInMember(SyntaxNode member, string methodName, SemanticModel? model,
        List<ClosureCapture> results)
    {
        var lambdas = member.DescendantNodesAndSelf()
            .Where(n => n is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            .Where(n => !MemberBodyEnumerator.IsInsideNestedLocalFunction(n, member))
            .ToList();

        foreach (var lambda in lambdas)
        {
            var captured = model is not null
                ? GetCapturedVariablesSemantic(lambda, model)
                : GetCapturedVariablesSyntactic(lambda, member);

            if (captured.Count > 0)
            {
                var line = lambda.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                results.Add(new ClosureCapture(methodName, DescribeLambda(lambda), captured, line));
            }
        }
    }

    static string DescribeLambda(SyntaxNode lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax simple =>
            $"lambda ({simple.Parameter.Identifier.Text}) => ...",
        ParenthesizedLambdaExpressionSyntax paren =>
            $"lambda ({string.Join(", ", paren.ParameterList.Parameters.Select(p => p.Identifier.Text))}) => ...",
        AnonymousMethodExpressionSyntax => "anonymous method",
        _ => "lambda"
    };

    static HashSet<string> CollectLambdaParameters(SyntaxNode lambda)
    {
        var lambdaParams = new HashSet<string>(StringComparer.Ordinal);
        switch (lambda)
        {
            case SimpleLambdaExpressionSyntax simple:
                lambdaParams.Add(simple.Parameter.Identifier.Text);
                break;
            case ParenthesizedLambdaExpressionSyntax paren:
                foreach (var p in paren.ParameterList.Parameters)
                    lambdaParams.Add(p.Identifier.Text);
                break;
            case AnonymousMethodExpressionSyntax anon when anon.ParameterList is not null:
                foreach (var p in anon.ParameterList.Parameters)
                    lambdaParams.Add(p.Identifier.Text);
                break;
        }
        return lambdaParams;
    }

    static IReadOnlyList<string> GetCapturedVariablesSemantic(SyntaxNode lambda, SemanticModel model)
    {
        var captured = new HashSet<string>(StringComparer.Ordinal);
        var lambdaParams = CollectLambdaParameters(lambda);

        foreach (var identifier in lambda.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var name = identifier.Identifier.Text;
            if (lambdaParams.Contains(name))
                continue;

            var capturedName = ResolveCapturedName(lambda, model.GetSymbolInfo(identifier).Symbol, name);
            if (capturedName is not null)
                captured.Add(capturedName);
        }

        return captured.Order().ToList();
    }

    // Locals/parameters count as captures when declared outside the lambda;
    // instance member access implies a 'this' capture.
    static string? ResolveCapturedName(SyntaxNode lambda, ISymbol? symbol, string name) => symbol switch
    {
        ILocalSymbol or IParameterSymbol when IsDeclaredOutsideLambda(lambda, symbol) => name,
        IFieldSymbol or IPropertySymbol when !symbol.IsStatic => "this",
        _ => null
    };

    static bool IsDeclaredOutsideLambda(SyntaxNode lambda, ISymbol symbol) =>
        !lambda.Span.Contains(symbol.DeclaringSyntaxReferences.FirstOrDefault()?.Span ?? default);

    static IReadOnlyList<string> GetCapturedVariablesSyntactic(SyntaxNode lambda, SyntaxNode method)
    {
        var outerNames = CollectOuterNames(lambda, method);
        var lambdaParams = CollectLambdaParameters(lambda);

        var captured = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in lambda.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var name = identifier.Identifier.Text;
            if (!lambdaParams.Contains(name) && outerNames.Contains(name))
                captured.Add(name);
        }

        return captured.Order().ToList();
    }

    // Member-level names visible to the lambda: parameters + locals declared outside it.
    static HashSet<string> CollectOuterNames(SyntaxNode lambda, SyntaxNode member)
    {
        var outerNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var p in GetParameters(member))
            outerNames.Add(p.Identifier.Text);

        var scopeRoot = GetLocalScopeRoot(member);
        foreach (var local in scopeRoot.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (!lambda.Span.Contains(local.Span))
                outerNames.Add(local.Identifier.Text);
        }

        if (member is ExpressionSyntax)
        {
            var typeDecl = member.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (typeDecl is not null)
            {
                foreach (var field in typeDecl.Members.OfType<FieldDeclarationSyntax>())
                {
                    foreach (var variable in field.Declaration.Variables)
                        outerNames.Add(variable.Identifier.Text);
                }
            }
        }

        return outerNames;
    }

    static SyntaxNode GetLocalScopeRoot(SyntaxNode member) => member switch
    {
        LocalFunctionStatementSyntax => FindEnclosingExecutableMember(member) ?? member,
        _ => member
    };

    static SyntaxNode? FindEnclosingExecutableMember(SyntaxNode member) =>
        member.Ancestors().FirstOrDefault(a => a is MethodDeclarationSyntax
            or ConstructorDeclarationSyntax
            or AccessorDeclarationSyntax
            or OperatorDeclarationSyntax
            or ConversionOperatorDeclarationSyntax
            or LocalFunctionStatementSyntax);

    static IEnumerable<ParameterSyntax> GetParameters(SyntaxNode member) => member switch
    {
        MethodDeclarationSyntax m => m.ParameterList.Parameters,
        ConstructorDeclarationSyntax c => c.ParameterList.Parameters,
        OperatorDeclarationSyntax o => o.ParameterList.Parameters,
        ConversionOperatorDeclarationSyntax co => co.ParameterList.Parameters,
        LocalFunctionStatementSyntax lf => lf.ParameterList?.Parameters ?? [],
        _ => []
    };
}
