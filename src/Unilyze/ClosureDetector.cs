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

        foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            var methodName = method.Identifier.Text;
            DetectInMember(method, methodName, model, results);
        }

        foreach (var ctor in typeDecl.Members.OfType<ConstructorDeclarationSyntax>())
        {
            var methodName = ctor.Identifier.Text + ".ctor";
            DetectInMember(ctor, methodName, model, results);
        }

        return results;
    }

    static void DetectInMember(SyntaxNode member, string methodName, SemanticModel? model,
        List<ClosureCapture> results)
    {
        var lambdas = member.DescendantNodes()
            .Where(n => n is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            .ToList();

        foreach (var lambda in lambdas)
        {
            var captured = model is not null
                ? GetCapturedVariablesSemantic(lambda, model)
                : GetCapturedVariablesSyntactic(lambda, member);

            if (captured.Count > 0)
            {
                var line = lambda.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var description = lambda switch
                {
                    SimpleLambdaExpressionSyntax simple =>
                        $"lambda ({simple.Parameter.Identifier.Text}) => ...",
                    ParenthesizedLambdaExpressionSyntax paren =>
                        $"lambda ({string.Join(", ", paren.ParameterList.Parameters.Select(p => p.Identifier.Text))}) => ...",
                    AnonymousMethodExpressionSyntax => "anonymous method",
                    _ => "lambda"
                };
                results.Add(new ClosureCapture(methodName, description, captured, line));
            }
        }
    }

    static IReadOnlyList<string> GetCapturedVariablesSemantic(SyntaxNode lambda, SemanticModel model)
    {
        var captured = new HashSet<string>(StringComparer.Ordinal);

        // Collect lambda's own parameter names
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

        foreach (var identifier in lambda.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var name = identifier.Identifier.Text;
            if (lambdaParams.Contains(name))
                continue;

            var symbolInfo = model.GetSymbolInfo(identifier);
            var symbol = symbolInfo.Symbol;
            if (symbol is null)
                continue;

            switch (symbol)
            {
                case ILocalSymbol local:
                    // Check if declared outside the lambda
                    if (!lambda.Span.Contains(local.DeclaringSyntaxReferences
                        .FirstOrDefault()?.Span ?? default))
                    {
                        captured.Add(name);
                    }
                    break;
                case IParameterSymbol param:
                    // Method parameter (declared outside lambda)
                    if (!lambda.Span.Contains(param.DeclaringSyntaxReferences
                        .FirstOrDefault()?.Span ?? default))
                    {
                        captured.Add(name);
                    }
                    break;
                case IFieldSymbol or IPropertySymbol:
                    // Instance member access implies 'this' capture
                    if (!symbol.IsStatic)
                    {
                        captured.Add("this");
                    }
                    break;
            }
        }

        return captured.Order().ToList();
    }

    static IReadOnlyList<string> GetCapturedVariablesSyntactic(SyntaxNode lambda, SyntaxNode method)
    {
        // Collect method-level names (parameters + local declarations)
        var outerNames = new HashSet<string>(StringComparer.Ordinal);

        // Method parameters
        switch (method)
        {
            case MethodDeclarationSyntax m:
                foreach (var p in m.ParameterList.Parameters)
                    outerNames.Add(p.Identifier.Text);
                break;
            case ConstructorDeclarationSyntax c:
                foreach (var p in c.ParameterList.Parameters)
                    outerNames.Add(p.Identifier.Text);
                break;
        }

        // Local variable declarations outside lambda
        foreach (var local in method.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (!lambda.Span.Contains(local.Span))
                outerNames.Add(local.Identifier.Text);
        }

        // Lambda's own parameters
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

        // Check identifiers inside lambda
        var captured = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in lambda.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var name = identifier.Identifier.Text;
            if (!lambdaParams.Contains(name) && outerNames.Contains(name))
                captured.Add(name);
        }

        return captured.Order().ToList();
    }
}
