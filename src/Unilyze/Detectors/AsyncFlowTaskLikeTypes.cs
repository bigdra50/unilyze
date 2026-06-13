namespace Unilyze.Detectors;

using Microsoft.CodeAnalysis;

internal static class AsyncFlowTaskLikeTypes
{
    public static bool IsTaskLike(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
            return false;

        var definition = namedType.OriginalDefinition ?? namedType;
        return IsSystemTaskType(definition) || IsUniTaskType(definition);
    }

    static bool IsSystemTaskType(INamedTypeSymbol definition)
    {
        if (!IsNamespace(definition, "System.Threading.Tasks", "global::System.Threading.Tasks"))
            return false;

        return definition.Name is "Task" or "ValueTask";
    }

    static bool IsUniTaskType(INamedTypeSymbol definition)
    {
        if (!IsNamespace(definition, "Cysharp.Threading.Tasks", "global::Cysharp.Threading.Tasks"))
            return false;

        return definition.Name is "UniTask";
    }

    static bool IsNamespace(INamedTypeSymbol definition, string ns, string globalNs)
    {
        var containing = definition.ContainingNamespace?.ToDisplayString();
        return containing == ns || containing == globalNs;
    }
}
