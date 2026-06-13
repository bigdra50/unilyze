namespace Unilyze.DI;

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
                        typeArgs[0].Name, typeArgs[1].Name, "VContainer", lifetime, filePath, line,
                        DISymbolNaming.Qualify(typeArgs[0]), DISymbolNaming.Qualify(typeArgs[1]));
                if (typeArgs.Length == 1)
                    return new DIRegistration(
                        typeArgs[0].Name, typeArgs[0].Name, "VContainer", lifetime, filePath, line,
                        DISymbolNaming.Qualify(typeArgs[0]), DISymbolNaming.Qualify(typeArgs[0]));
                return null;
            }
            case "RegisterInstance":
            {
                var (implType, implQualified) = method.TypeArguments.Length > 0
                    ? (method.TypeArguments[0].Name, DISymbolNaming.Qualify(method.TypeArguments[0]))
                    : InferInstanceType(invocation, method);
                return new DIRegistration(
                    implType, implType, "VContainer", "Singleton", filePath, line,
                    implQualified, implQualified);
            }
            case "RegisterFactory":
            {
                var (factoryType, factoryQualified) = method.TypeArguments.Length > 0
                    ? (method.TypeArguments[0].Name, DISymbolNaming.Qualify(method.TypeArguments[0]))
                    : ("Unknown", (string?)null);
                return new DIRegistration(
                    factoryType, factoryType, "VContainer", "Transient", filePath, line,
                    factoryQualified, factoryQualified);
            }
            default:
                return null;
        }
    }

    private static (string Name, string? Qualified) InferInstanceType(
        InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (invocation.ArgumentList.Arguments.Count > 0)
        {
            var argType = method.Parameters.FirstOrDefault()?.Type;
            if (argType is not null)
                return (argType.Name, DISymbolNaming.Qualify(argType));
        }
        return ("Unknown", null);
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
                    return new DIRegistration(
                        DISymbolNaming.SimpleName(typeArgs[0]), DISymbolNaming.SimpleName(typeArgs[1]),
                        "VContainer", lifetime, filePath, line,
                        DISymbolNaming.QualifiedFromSyntax(typeArgs[0]), DISymbolNaming.QualifiedFromSyntax(typeArgs[1]));
                if (typeArgs.Count == 1)
                    return new DIRegistration(
                        DISymbolNaming.SimpleName(typeArgs[0]), DISymbolNaming.SimpleName(typeArgs[0]),
                        "VContainer", lifetime, filePath, line,
                        DISymbolNaming.QualifiedFromSyntax(typeArgs[0]), DISymbolNaming.QualifiedFromSyntax(typeArgs[0]));
                return null;
            }
            case "RegisterInstance":
            {
                var instType = InferInstanceTypeSyntactic(invocation);
                return new DIRegistration(
                    DISymbolNaming.SimpleName(instType), DISymbolNaming.SimpleName(instType),
                    "VContainer", "Singleton", filePath, line,
                    DISymbolNaming.QualifiedFromSyntax(instType), DISymbolNaming.QualifiedFromSyntax(instType));
            }
            case "RegisterFactory":
            {
                var factoryType = typeArgs.Count > 0 ? typeArgs[0] : "Unknown";
                return new DIRegistration(
                    DISymbolNaming.SimpleName(factoryType), DISymbolNaming.SimpleName(factoryType),
                    "VContainer", "Transient", filePath, line,
                    DISymbolNaming.QualifiedFromSyntax(factoryType), DISymbolNaming.QualifiedFromSyntax(factoryType));
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
