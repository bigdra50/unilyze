using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public sealed record ParamsAllocation(
    string MethodName, string CalledMethod, int ArgCount, int Line);

public static class ParamsArrayDetector
{
    public static IReadOnlyList<ParamsAllocation> Detect(
        TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (model is null)
            return [];

        var results = new List<ParamsAllocation>();

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

    static void DetectInMember(SyntaxNode member, string methodName, SemanticModel model,
        List<ParamsAllocation> results)
    {
        foreach (var invocation in member.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbolInfo = model.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol calledMethod)
                continue;

            var parameters = calledMethod.Parameters;
            if (parameters.Length == 0)
                continue;

            var lastParam = parameters[^1];
            if (!lastParam.IsParams)
                continue;

            var fixedParamCount = parameters.Length - 1;
            var arguments = invocation.ArgumentList.Arguments;
            var argCount = arguments.Count;

            // If exactly one argument maps to the params position and it's already an array, skip
            if (argCount == fixedParamCount + 1)
            {
                var lastArg = arguments[fixedParamCount];
                var argTypeInfo = model.GetTypeInfo(lastArg.Expression);
                if (argTypeInfo.Type is IArrayTypeSymbol)
                    continue;
            }

            // params expansion occurs: implicit array allocation
            var paramsArgCount = argCount - fixedParamCount;
            var calledMethodName = calledMethod.Name;
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                calledMethodName = $"{memberAccess.Expression}.{calledMethod.Name}";

            var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            results.Add(new ParamsAllocation(methodName, calledMethodName, paramsArgCount, line));
        }
    }
}
