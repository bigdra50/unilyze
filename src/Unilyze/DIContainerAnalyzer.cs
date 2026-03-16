namespace Unilyze;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public sealed record DIRegistration(
    string ServiceType,
    string ImplementationType,
    string ContainerType,
    string? Lifetime,
    string FilePath,
    int Line);

public static class DIContainerAnalyzer
{
    public static IReadOnlyList<DIRegistration> Analyze(
        IReadOnlyList<SyntaxTree> syntaxTrees,
        Compilation? compilation)
    {
        var results = new List<DIRegistration>();

        foreach (var tree in syntaxTrees)
        {
            var model = compilation?.GetSemanticModel(tree);
            var root = tree.GetRoot();
            var filePath = tree.FilePath ?? "";

            CollectInvocationRegistrations(root, model, filePath, results);
            CollectInjectAttributes(root, model, filePath, results);
        }

        return results;
    }

    static void CollectInvocationRegistrations(
        SyntaxNode root, SemanticModel? model, string filePath, List<DIRegistration> results)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var reg = model is not null
                ? TryResolveSemantic(invocation, model, filePath)
                : TryResolveSyntactic(invocation, filePath);

            if (reg is not null)
                results.Add(reg);
        }
    }

    // --- Semantic path ---

    static DIRegistration? TryResolveSemantic(
        InvocationExpressionSyntax invocation, SemanticModel model, string filePath)
    {
        var symbolInfo = model.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return null;

        var containingNs = GetRootNamespace(methodSymbol.ContainingType);

        return containingNs switch
        {
            "VContainer" => TryResolveVContainerSemantic(invocation, methodSymbol, filePath),
            "Zenject" => TryResolveZenjectSemantic(invocation, methodSymbol, filePath),
            _ => null
        };
    }

    static string GetRootNamespace(INamedTypeSymbol? type)
    {
        if (type is null) return "";
        var ns = type.ContainingNamespace;
        while (ns is { IsGlobalNamespace: false })
        {
            if (ns.ContainingNamespace is { IsGlobalNamespace: true })
                return ns.Name;
            ns = ns.ContainingNamespace;
        }
        return "";
    }

    static DIRegistration? TryResolveVContainerSemantic(
        InvocationExpressionSyntax invocation, IMethodSymbol method, string filePath)
    {
        var name = method.Name;
        var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        switch (name)
        {
            case "Register":
            {
                var typeArgs = method.TypeArguments;
                var lifetime = ExtractVContainerLifetimeFromArgs(invocation);
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

    static string InferInstanceType(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        if (invocation.ArgumentList.Arguments.Count > 0)
        {
            var argType = method.Parameters.FirstOrDefault()?.Type;
            if (argType is not null)
                return argType.Name;
        }
        return "Unknown";
    }

    static DIRegistration? TryResolveZenjectSemantic(
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
                var (implType, lifetime) = TraceZenjectChainSemantic(invocation);
                return new DIRegistration(
                    serviceType, implType ?? serviceType, "Zenject", lifetime, filePath, line);
            }
            case "BindInterfacesTo":
            {
                var implType = method.TypeArguments.Length > 0
                    ? method.TypeArguments[0].Name
                    : "Unknown";
                var (_, lifetime) = TraceZenjectChainSemantic(invocation);
                return new DIRegistration(
                    implType, implType, "Zenject", lifetime, filePath, line);
            }
            case "BindInterfacesAndSelfTo":
            {
                var implType = method.TypeArguments.Length > 0
                    ? method.TypeArguments[0].Name
                    : "Unknown";
                var (_, lifetime) = TraceZenjectChainSemantic(invocation);
                return new DIRegistration(
                    implType, implType, "Zenject", lifetime, filePath, line);
            }
            default:
                return null;
        }
    }

    static (string? ImplType, string? Lifetime) TraceZenjectChainSemantic(
        InvocationExpressionSyntax startInvocation)
    {
        string? implType = null;
        string? lifetime = null;

        var current = startInvocation.Parent;
        while (current is not null)
        {
            if (current is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Parent is InvocationExpressionSyntax chainInvocation)
            {
                var chainName = memberAccess.Name;
                switch (chainName.Identifier.Text)
                {
                    case "To":
                        if (chainName is GenericNameSyntax toGeneric && toGeneric.TypeArgumentList.Arguments.Count > 0)
                            implType = toGeneric.TypeArgumentList.Arguments[0].ToString();
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
                current = chainInvocation.Parent;
            }
            else
            {
                break;
            }
        }

        return (implType, lifetime);
    }

    // --- Syntactic fallback ---

    static DIRegistration? TryResolveSyntactic(InvocationExpressionSyntax invocation, string filePath)
    {
        var (receiverName, methodName, typeArgs) = DecomposeInvocation(invocation);
        if (methodName is null) return null;

        var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        // VContainer patterns
        switch (methodName)
        {
            case "Register":
            {
                var lifetime = ExtractVContainerLifetimeFromArgs(invocation);
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
        }

        // Zenject patterns
        switch (methodName)
        {
            case "Bind":
            {
                var serviceType = typeArgs.Count > 0 ? typeArgs[0] : "Unknown";
                var (implType, lifetime) = TraceZenjectChainSyntactic(invocation);
                return new DIRegistration(serviceType, implType ?? serviceType, "Zenject", lifetime, filePath, line);
            }
            case "BindInterfacesTo":
            {
                var implType = typeArgs.Count > 0 ? typeArgs[0] : "Unknown";
                var (_, lifetime) = TraceZenjectChainSyntactic(invocation);
                return new DIRegistration(implType, implType, "Zenject", lifetime, filePath, line);
            }
            case "BindInterfacesAndSelfTo":
            {
                var implType = typeArgs.Count > 0 ? typeArgs[0] : "Unknown";
                var (_, lifetime) = TraceZenjectChainSyntactic(invocation);
                return new DIRegistration(implType, implType, "Zenject", lifetime, filePath, line);
            }
        }

        return null;
    }

    static (string? Receiver, string? MethodName, IReadOnlyList<string> TypeArgs) DecomposeInvocation(
        InvocationExpressionSyntax invocation)
    {
        switch (invocation.Expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
            {
                var receiver = memberAccess.Expression.ToString();
                return memberAccess.Name switch
                {
                    GenericNameSyntax generic => (receiver, generic.Identifier.Text,
                        generic.TypeArgumentList.Arguments.Select(a => a.ToString()).ToList()),
                    IdentifierNameSyntax id => (receiver, id.Identifier.Text, []),
                    _ => (receiver, memberAccess.Name.ToString(), [])
                };
            }
            case GenericNameSyntax generic:
                return (null, generic.Identifier.Text,
                    generic.TypeArgumentList.Arguments.Select(a => a.ToString()).ToList());
            case IdentifierNameSyntax id:
                return (null, id.Identifier.Text, []);
            default:
                return (null, null, []);
        }
    }

    static string? ExtractVContainerLifetimeFromArgs(InvocationExpressionSyntax invocation)
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

    static string InferInstanceTypeSyntactic(InvocationExpressionSyntax invocation)
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

    static (string? ImplType, string? Lifetime) TraceZenjectChainSyntactic(
        InvocationExpressionSyntax startInvocation)
    {
        string? implType = null;
        string? lifetime = null;

        var current = startInvocation.Parent;
        while (current is not null)
        {
            if (current is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Parent is InvocationExpressionSyntax chainInvocation)
            {
                var chainName = memberAccess.Name;
                switch (chainName.Identifier.Text)
                {
                    case "To":
                        if (chainName is GenericNameSyntax toGeneric && toGeneric.TypeArgumentList.Arguments.Count > 0)
                            implType = toGeneric.TypeArgumentList.Arguments[0].ToString();
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
                current = chainInvocation.Parent;
            }
            else
            {
                break;
            }
        }

        return (implType, lifetime);
    }

    // --- Inject attribute detection ---

    static void CollectInjectAttributes(
        SyntaxNode root, SemanticModel? model, string filePath, List<DIRegistration> results)
    {
        foreach (var attrSyntax in root.DescendantNodes().OfType<AttributeSyntax>())
        {
            var attrName = attrSyntax.Name.ToString();
            if (!IsInjectAttribute(attrName, attrSyntax, model))
                continue;

            var containerType = ResolveInjectContainerType(attrSyntax, model);
            var line = attrSyntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

            var targetType = GetInjectTargetType(attrSyntax);
            results.Add(new DIRegistration(
                targetType, targetType, containerType, null, filePath, line));
        }
    }

    static bool IsInjectAttribute(string attrName, AttributeSyntax attrSyntax, SemanticModel? model)
    {
        // Quick syntactic check
        if (attrName is not ("Inject" or "InjectAttribute"
            or "VContainer.Inject" or "VContainer.InjectAttribute"
            or "Zenject.Inject" or "Zenject.InjectAttribute"
            or "Zenject.InjectOptional" or "Zenject.InjectOptionalAttribute"))
            return false;

        if (model is null)
            return true;

        var symbolInfo = model.GetSymbolInfo(attrSyntax);
        if (symbolInfo.Symbol is IMethodSymbol ctorSymbol)
        {
            var ns = GetRootNamespace(ctorSymbol.ContainingType);
            return ns is "VContainer" or "Zenject";
        }

        return true;
    }

    static string ResolveInjectContainerType(AttributeSyntax attrSyntax, SemanticModel? model)
    {
        if (model is not null)
        {
            var symbolInfo = model.GetSymbolInfo(attrSyntax);
            if (symbolInfo.Symbol is IMethodSymbol ctorSymbol)
            {
                var ns = GetRootNamespace(ctorSymbol.ContainingType);
                if (ns is "VContainer") return "VContainer";
                if (ns is "Zenject") return "Zenject";
            }
        }

        var name = attrSyntax.Name.ToString();
        if (name.StartsWith("VContainer")) return "VContainer";
        if (name.StartsWith("Zenject")) return "Zenject";
        return "Unknown";
    }

    static string GetInjectTargetType(AttributeSyntax attrSyntax)
    {
        var parent = attrSyntax.Parent?.Parent;
        return parent switch
        {
            FieldDeclarationSyntax field => field.Declaration.Type.ToString(),
            PropertyDeclarationSyntax prop => prop.Type.ToString(),
            MethodDeclarationSyntax method => method.Identifier.Text,
            ParameterSyntax param => param.Type?.ToString() ?? "Unknown",
            _ => "Unknown"
        };
    }
}
