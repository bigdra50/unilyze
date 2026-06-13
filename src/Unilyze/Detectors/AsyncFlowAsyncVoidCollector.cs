namespace Unilyze.Detectors;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class AsyncFlowAsyncVoidCollector
{
    static readonly HashSet<string> UnityMessageMethodNames = new(StringComparer.Ordinal)
    {
        "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy",
        "Update", "FixedUpdate", "LateUpdate", "OnGUI",
        "OnApplicationQuit", "OnApplicationPause", "OnApplicationFocus",
        "OnTriggerEnter", "OnTriggerExit", "OnTriggerStay",
        "OnCollisionEnter", "OnCollisionExit", "OnCollisionStay",
        "OnTriggerEnter2D", "OnTriggerExit2D", "OnTriggerStay2D",
        "OnCollisionEnter2D", "OnCollisionExit2D", "OnCollisionStay2D",
        "OnMouseDown", "OnMouseUp", "OnMouseEnter", "OnMouseExit",
        "OnMouseOver", "OnMouseDrag", "OnBecameVisible", "OnBecameInvisible",
    };

    public static List<AsyncVoidOccurrence> Collect(
        TypeDeclarationSyntax typeDecl,
        SemanticModel? model,
        bool isMonoBehaviour)
    {
        var asyncVoidMethods = new List<AsyncVoidOccurrence>();
        CollectFromMethods(typeDecl, model, isMonoBehaviour, asyncVoidMethods);
        CollectFromLocalFunctions(typeDecl, asyncVoidMethods);
        return asyncVoidMethods;
    }

    static void CollectFromMethods(
        TypeDeclarationSyntax typeDecl,
        SemanticModel? model,
        bool isMonoBehaviour,
        List<AsyncVoidOccurrence> results)
    {
        foreach (var method in typeDecl.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (ShouldReportAsyncVoidMethod(method, model, isMonoBehaviour))
                results.Add(CreateOccurrence(method.Identifier));
        }
    }

    static bool ShouldReportAsyncVoidMethod(
        MethodDeclarationSyntax method,
        SemanticModel? model,
        bool isMonoBehaviour)
    {
        if (!IsAsyncVoid(method))
            return false;

        if (AsyncFlowEventHandlerMatcher.IsEventHandlerSignature(method.ParameterList, model))
            return false;

        return !isMonoBehaviour || !UnityMessageMethodNames.Contains(method.Identifier.Text);
    }

    static void CollectFromLocalFunctions(
        TypeDeclarationSyntax typeDecl,
        List<AsyncVoidOccurrence> results)
    {
        foreach (var localFn in typeDecl.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
        {
            if (!IsAsyncVoid(localFn))
                continue;

            results.Add(CreateOccurrence(localFn.Identifier));
        }
    }

    static AsyncVoidOccurrence CreateOccurrence(SyntaxToken identifier)
    {
        var line = identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        return new AsyncVoidOccurrence(identifier.Text, line);
    }

    static bool IsAsyncVoid(MethodDeclarationSyntax method)
        => method.Modifiers.Any(SyntaxKind.AsyncKeyword) && IsVoidReturnType(method.ReturnType);

    static bool IsAsyncVoid(LocalFunctionStatementSyntax localFn)
        => localFn.Modifiers.Any(SyntaxKind.AsyncKeyword) && IsVoidReturnType(localFn.ReturnType);

    static bool IsVoidReturnType(TypeSyntax returnType)
        => returnType is PredefinedTypeSyntax predefined
            && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword);
}
