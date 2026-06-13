namespace Unilyze.DI;

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
                var (serviceType, serviceQualified) = method.TypeArguments.Length > 0
                    ? (method.TypeArguments[0].Name, DISymbolNaming.Qualify(method.TypeArguments[0]))
                    : ("Unknown", (string?)null);
                var (implType, implQualified, lifetime) = TraceZenjectChain(invocation);
                return new DIRegistration(
                    serviceType, implType ?? serviceType, "Zenject", lifetime, filePath, line,
                    serviceQualified, implQualified ?? serviceQualified);
            }
            case "BindInterfacesTo":
            {
                var (implType, implQualified) = method.TypeArguments.Length > 0
                    ? (method.TypeArguments[0].Name, DISymbolNaming.Qualify(method.TypeArguments[0]))
                    : ("Unknown", (string?)null);
                var (_, _, lifetime) = TraceZenjectChain(invocation);
                return new DIRegistration(
                    implType, implType, "Zenject", lifetime, filePath, line,
                    implQualified, implQualified);
            }
            case "BindInterfacesAndSelfTo":
            {
                var (implType, implQualified) = method.TypeArguments.Length > 0
                    ? (method.TypeArguments[0].Name, DISymbolNaming.Qualify(method.TypeArguments[0]))
                    : ("Unknown", (string?)null);
                var (_, _, lifetime) = TraceZenjectChain(invocation);
                return new DIRegistration(
                    implType, implType, "Zenject", lifetime, filePath, line,
                    implQualified, implQualified);
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
                var serviceRaw = typeArgs.Count > 0 ? typeArgs[0] : "Unknown";
                var serviceType = DISymbolNaming.SimpleName(serviceRaw);
                var serviceQualified = DISymbolNaming.QualifiedFromSyntax(serviceRaw);
                var (implType, implQualified, lifetime) = TraceZenjectChain(invocation);
                return new DIRegistration(
                    serviceType, implType ?? serviceType, "Zenject", lifetime, filePath, line,
                    serviceQualified, implQualified ?? serviceQualified);
            }
            case "BindInterfacesTo":
            {
                var implRaw = typeArgs.Count > 0 ? typeArgs[0] : "Unknown";
                var implType = DISymbolNaming.SimpleName(implRaw);
                var implQualified = DISymbolNaming.QualifiedFromSyntax(implRaw);
                var (_, _, lifetime) = TraceZenjectChain(invocation);
                return new DIRegistration(
                    implType, implType, "Zenject", lifetime, filePath, line, implQualified, implQualified);
            }
            case "BindInterfacesAndSelfTo":
            {
                var implRaw = typeArgs.Count > 0 ? typeArgs[0] : "Unknown";
                var implType = DISymbolNaming.SimpleName(implRaw);
                var implQualified = DISymbolNaming.QualifiedFromSyntax(implRaw);
                var (_, _, lifetime) = TraceZenjectChain(invocation);
                return new DIRegistration(
                    implType, implType, "Zenject", lifetime, filePath, line, implQualified, implQualified);
            }
            default:
                return null;
        }
    }

    // Walks the fluent chain after Bind/BindInterfacesTo (e.g. .To<T>().AsSingle()).
    // Shared by the semantic and syntactic paths: the chain shape is purely syntactic,
    // so the impl type carries a qualified candidate only when written fully qualified.
    private static (string? ImplType, string? ImplQualified, string? Lifetime) TraceZenjectChain(
        InvocationExpressionSyntax startInvocation)
    {
        string? implType = null;
        string? implQualified = null;
        string? lifetime = null;

        var current = startInvocation.Parent;
        while (current is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax chainInvocation } memberAccess)
        {
            ApplyZenjectChainLink(memberAccess.Name, ref implType, ref implQualified, ref lifetime);
            current = chainInvocation.Parent;
        }

        return (implType, implQualified, lifetime);
    }

    private static void ApplyZenjectChainLink(
        SimpleNameSyntax chainName, ref string? implType, ref string? implQualified, ref string? lifetime)
    {
        switch (chainName.Identifier.Text)
        {
            case "To":
                if (chainName is GenericNameSyntax { TypeArgumentList.Arguments: [var firstArg, ..] })
                {
                    var raw = firstArg.ToString();
                    implType = DISymbolNaming.SimpleName(raw);
                    implQualified = DISymbolNaming.QualifiedFromSyntax(raw);
                }
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
