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
    int Line,
    string? ServiceTypeQualified = null,
    string? ImplementationTypeQualified = null);

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

        // The invocation does not bind to any method (e.g. VContainer/Zenject is an
        // external package not referenced in the compilation, so the receiver is
        // object/unresolved). Fall back to name-based matching so DI edges are still
        // detected. A resolved-but-foreign method symbol is treated as authoritative
        // negative below, preventing false positives from unrelated Register/Bind APIs.
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return TryResolveSyntactic(invocation, filePath);

        var containingNs = GetRootNamespace(methodSymbol.ContainingType);

        return containingNs switch
        {
            "VContainer" => VContainerRegistrationResolver.ResolveSemantic(invocation, methodSymbol, filePath),
            "Zenject" => ZenjectRegistrationResolver.ResolveSemantic(invocation, methodSymbol, filePath),
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

    // --- Syntactic fallback ---

    static DIRegistration? TryResolveSyntactic(InvocationExpressionSyntax invocation, string filePath)
    {
        var (receiverName, methodName, typeArgs) = DecomposeInvocation(invocation);
        if (methodName is null) return null;

        var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        return VContainerRegistrationResolver.ResolveSyntactic(invocation, methodName, typeArgs, filePath, line)
            ?? ZenjectRegistrationResolver.ResolveSyntactic(invocation, methodName, typeArgs, filePath, line);
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
        return ResolveContainerBySymbol(attrSyntax, model)
            ?? ResolveContainerByName(attrSyntax.Name.ToString())
            ?? "Unknown";
    }

    static string? ResolveContainerBySymbol(AttributeSyntax attrSyntax, SemanticModel? model)
    {
        if (model?.GetSymbolInfo(attrSyntax).Symbol is not IMethodSymbol ctorSymbol)
            return null;

        return GetRootNamespace(ctorSymbol.ContainingType) switch
        {
            "VContainer" => "VContainer",
            "Zenject" => "Zenject",
            _ => null
        };
    }

    static string? ResolveContainerByName(string name)
    {
        if (name.StartsWith("VContainer")) return "VContainer";
        if (name.StartsWith("Zenject")) return "Zenject";
        return null;
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
