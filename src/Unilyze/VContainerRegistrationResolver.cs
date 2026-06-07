namespace Unilyze;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class VContainerRegistrationResolver
{
    internal static DIRegistration? ResolveSemantic(
        InvocationExpressionSyntax invocation, IMethodSymbol method, string filePath)
    {
        var name = method.Name;
        var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        switch (name)
        {
            case "Register":
            {
                var typeArgs = method.TypeArguments;
                var lifetime = ExtractLifetimeFromArgs(invocation);
                if (typeArgs.Length == 2)
                    return new DIRegistration(
                        typeArgs[0].Name, typeArgs[1].Name, "VContainer", lifetime, filePath, line);
                if (typeArgs.Length == 1)
                    return new DIRegistration(
                        typeArgs[0].Name, typeArgs[0].Name, "VContainer", lifetime, filePath, line);
                return null;
            }
            case "RegisterInstance":
            {
                var implType = method.TypeArguments.Length > 0
                    ? method.TypeArguments[0].Name
                    : InferInstanceType(invocation, method);
                return new DIRegistration(
                    implType, implType, "VContainer", "Singleton", filePath, line);
            }
            case "RegisterFactory":
            {
                var factoryType = method.TypeArguments.Length > 0
                    ? method.TypeArguments[0].Name
                    : "Unknown";
                return new DIRegistration(
                    factoryType, factoryType, "VContainer", "Transient", filePath, line);
            }
            default:
                return null;
        }
    }

    private static string InferInstanceType(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (invocation.ArgumentList.Arguments.Count > 0)
        {
            var argType = method.Parameters.FirstOrDefault()?.Type;
            if (argType is not null)
                return argType.Name;
        }
        return "Unknown";
    }

    internal static DIRegistration? ResolveSyntactic(
        InvocationExpressionSyntax invocation, string methodName, IReadOnlyList<string> typeArgs,
        string filePath, int line)
    {
        switch (methodName)
        {
            case "Register":
            {
                var lifetime = ExtractLifetimeFromArgs(invocation);
                if (typeArgs.Count == 2)
                    return new DIRegistration(typeArgs[0], typeArgs[1], "VContainer", lifetime, filePath, line);
                if (typeArgs.Count == 1)
                    return new DIRegistration(typeArgs[0], typeArgs[0], "VContainer", lifetime, filePath, line);
                return null;
            }
            case "RegisterInstance":
                return new DIRegistration(
                    InferInstanceTypeSyntactic(invocation),
                    InferInstanceTypeSyntactic(invocation),
                    "VContainer", "Singleton", filePath, line);
            case "RegisterFactory":
            {
                var factoryType = typeArgs.Count > 0 ? typeArgs[0] : "Unknown";
                return new DIRegistration(factoryType, factoryType, "VContainer", "Transient", filePath, line);
            }
            default:
                return null;
        }
    }

    private static string? ExtractLifetimeFromArgs(InvocationExpressionSyntax invocation)
    {
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            var text = arg.Expression.ToString();
            if (text.Contains("Singleton")) return "Singleton";
            if (text.Contains("Transient")) return "Transient";
            if (text.Contains("Scoped")) return "Scoped";
        }
        return null;
    }

    private static string InferInstanceTypeSyntactic(InvocationExpressionSyntax invocation)
    {
        // Try generic type arg first
        if (invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax generic })
        {
            if (generic.TypeArgumentList.Arguments.Count > 0)
                return generic.TypeArgumentList.Arguments[0].ToString();
        }

        // Fall back to argument expression type name
        if (invocation.ArgumentList.Arguments.Count > 0)
        {
            var argExpr = invocation.ArgumentList.Arguments[0].Expression.ToString();
            return argExpr;
        }

        return "Unknown";
    }
}
