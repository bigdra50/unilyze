namespace Unilyze;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public sealed record AsyncVoidOccurrence(string MethodName, int Line);
public sealed record BlockingWaitOccurrence(string MethodName, int Line, string Pattern);
public sealed record AsyncFlowResult(
    IReadOnlyList<AsyncVoidOccurrence> AsyncVoidMethods,
    IReadOnlyList<BlockingWaitOccurrence> BlockingWaits);

public static class AsyncFlowAnalyzer
{
    public static AsyncFlowResult Analyze(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        var isMonoBehaviour = UnityContextClassifier.Classify(typeDecl, model).IsMonoBehaviour;
        var asyncVoidMethods = AsyncFlowAsyncVoidCollector.Collect(typeDecl, model, isMonoBehaviour);
        var blockingWaits = AsyncFlowBlockingWaitCollector.Collect(typeDecl, model);
        return new AsyncFlowResult(asyncVoidMethods, blockingWaits);
    }
}
