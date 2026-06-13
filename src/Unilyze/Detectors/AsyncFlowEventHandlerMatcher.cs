namespace Unilyze.Detectors;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class AsyncFlowEventHandlerMatcher
{
    public static bool IsEventHandlerSignature(ParameterListSyntax? parameterList, SemanticModel? model)
    {
        if (parameterList is not { Parameters.Count: 2 })
            return false;

        var first = parameterList.Parameters[0];
        var second = parameterList.Parameters[1];
        return first.Type is not null
            && second.Type is not null
            && (model is null
                ? IsEventHandlerSignatureSyntaxFallback(first, second)
                : IsSemanticEventHandler(first, second, model));
    }

    static bool IsSemanticEventHandler(ParameterSyntax first, ParameterSyntax second, SemanticModel model)
    {
        var firstType = model.GetTypeInfo(first.Type!).Type;
        var secondType = model.GetTypeInfo(second.Type!).Type;

        if (firstType is null || secondType is null)
            return IsEventHandlerSignatureSyntaxFallback(first, second);

        return firstType.SpecialType == SpecialType.System_Object
            && DerivesFromEventArgs(secondType);
    }

    static bool DerivesFromEventArgs(ITypeSymbol type)
    {
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType!)
        {
            if (current.Name == "EventArgs" && IsSystemNamespace(current))
                return true;
        }

        return false;
    }

    static bool IsSystemNamespace(ITypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString();
        return ns is "System" or "global::System";
    }

    static bool IsEventHandlerSignatureSyntaxFallback(ParameterSyntax first, ParameterSyntax second)
    {
        var firstName = first.Type!.ToString();
        if (firstName is not "object" and not "System.Object")
            return false;

        return second.Type!.ToString().EndsWith("EventArgs", StringComparison.Ordinal);
    }
}
