namespace Unilyze;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class ZenjectRegistrationResolver
{
    internal static DIRegistration? ResolveSemantic(
        InvocationExpressionSyntax invocation, IMethodSymbol method, string filePath)
    {
        var name = method.Name;
        var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        switch (name)
        {
            case "Bind":
            {
                var serviceType = method.TypeArguments.Length > 0
                    ? method.TypeArguments[0].Name
                    : "Unknown";
                var (implType, lifetime) = TraceZenjectChain(invocation);
                return new DIRegistration(
                    serviceType, implType ?? serviceType, "Zenject", lifetime, filePath, line);
            }
            case "BindInterfacesTo":
            {
                var implType = method.TypeArguments.Length > 0
                    ? method.TypeArguments[0].Name
                    : "Unknown";
                var (_, lifetime) = TraceZenjectChain(invocation);
                return new DIRegistration(
                    implType, implType, "Zenject", lifetime, filePath, line);
            }
            case "BindInterfacesAndSelfTo":
            {
                var implType = method.TypeArguments.Length > 0
                    ? method.TypeArguments[0].Name
                    : "Unknown";
                var (_, lifetime) = TraceZenjectChain(invocation);
                return new DIRegistration(
                    implType, implType, "Zenject", lifetime, filePath, line);
            }
            default:
                return null;
        }
    }

    internal static DIRegistration? ResolveSyntactic(
        InvocationExpressionSyntax invocation, string methodName, IReadOnlyList<string> typeArgs,
        string filePath, int line)
    {
        switch (methodName)
        {
            case "Bind":
            {
                var serviceType = typeArgs.Count > 0 ? typeArgs[0] : "Unknown";
                var (implType, lifetime) = TraceZenjectChain(invocation);
                return new DIRegistration(serviceType, implType ?? serviceType, "Zenject", lifetime, filePath, line);
            }
            case "BindInterfacesTo":
            {
                var implType = typeArgs.Count > 0 ? typeArgs[0] : "Unknown";
                var (_, lifetime) = TraceZenjectChain(invocation);
                return new DIRegistration(implType, implType, "Zenject", lifetime, filePath, line);
            }
            case "BindInterfacesAndSelfTo":
            {
                var implType = typeArgs.Count > 0 ? typeArgs[0] : "Unknown";
                var (_, lifetime) = TraceZenjectChain(invocation);
                return new DIRegistration(implType, implType, "Zenject", lifetime, filePath, line);
            }
            default:
                return null;
        }
    }

    // Walks the fluent chain after Bind/BindInterfacesTo (e.g. .To<T>().AsSingle()).
    // Shared by the semantic and syntactic paths: the chain shape is purely syntactic.
    private static (string? ImplType, string? Lifetime) TraceZenjectChain(
        InvocationExpressionSyntax startInvocation)
    {
        string? implType = null;
        string? lifetime = null;

        var current = startInvocation.Parent;
        while (current is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax chainInvocation } memberAccess)
        {
            ApplyZenjectChainLink(memberAccess.Name, ref implType, ref lifetime);
            current = chainInvocation.Parent;
        }

        return (implType, lifetime);
    }

    private static void ApplyZenjectChainLink(SimpleNameSyntax chainName, ref string? implType, ref string? lifetime)
    {
        switch (chainName.Identifier.Text)
        {
            case "To":
                if (chainName is GenericNameSyntax { TypeArgumentList.Arguments: [var firstArg, ..] })
                    implType = firstArg.ToString();
                break;
            case "AsSingle":
                lifetime = "Singleton";
                break;
            case "AsTransient":
                lifetime = "Transient";
                break;
            case "AsCached":
                lifetime = "Scoped";
                break;
        }
    }
}
