using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public static class RfcCalculator
{
    public static int Calculate(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        return model is not null
            ? CalculateSemantic(typeDecl, model)
            : CalculateSyntactic(typeDecl);
    }

    static int CalculateSemantic(TypeDeclarationSyntax typeDecl, SemanticModel model)
    {
        var methods = new List<MethodDeclarationSyntax>();
        var constructors = new List<ConstructorDeclarationSyntax>();

        foreach (var member in typeDecl.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method:
                    methods.Add(method);
                    break;
                case ConstructorDeclarationSyntax ctor:
                    constructors.Add(ctor);
                    break;
            }
        }

        int m = methods.Count + constructors.Count;

        var calledMethods = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var method in methods)
        {
            CollectInvokedSymbols(method, model, calledMethods);
        }

        foreach (var ctor in constructors)
        {
            CollectInvokedSymbols(ctor, model, calledMethods);
        }

        return m + calledMethods.Count;
    }

    static void CollectInvokedSymbols(SyntaxNode memberNode, SemanticModel model, HashSet<ISymbol> calledMethods)
    {
        foreach (var invocation in memberNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbolInfo = model.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
            {
                calledMethods.Add(methodSymbol.OriginalDefinition);
            }
        }
    }

    static int CalculateSyntactic(TypeDeclarationSyntax typeDecl)
    {
        var methods = new List<BaseMethodDeclarationSyntax>();

        foreach (var member in typeDecl.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method:
                    methods.Add(method);
                    break;
                case ConstructorDeclarationSyntax ctor:
                    methods.Add(ctor);
                    break;
            }
        }

        int m = methods.Count;

        var invokedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var method in methods)
        {
            foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = ExtractInvocationName(invocation);
                if (name is not null)
                    invokedNames.Add(name);
            }
        }

        return m + invokedNames.Count;
    }

    static string? ExtractInvocationName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text,
            _ => invocation.Expression.ToString()
        };
    }
}
