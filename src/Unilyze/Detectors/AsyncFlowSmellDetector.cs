using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Detectors;

internal sealed class AsyncFlowSmellDetector : ISmellDetector
{
    public IReadOnlyList<DetectedSmell> Detect(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        var typeName = SmellDetectorHelpers.GetTypeName(typeDecl);
        var flow = AsyncFlowAnalyzer.Analyze(typeDecl, model);
        return BuildSmells(typeName, flow);
    }

    static List<DetectedSmell> BuildSmells(string typeName, AsyncFlowResult flow)
    {
        var smells = new List<DetectedSmell>(flow.AsyncVoidMethods.Count + flow.BlockingWaits.Count);

        foreach (var occurrence in flow.AsyncVoidMethods)
            smells.Add(ToAsyncVoidSmell(typeName, occurrence));

        foreach (var occurrence in flow.BlockingWaits)
            smells.Add(ToBlockingWaitSmell(typeName, occurrence));

        return smells;
    }

    static DetectedSmell ToAsyncVoidSmell(string typeName, AsyncVoidOccurrence occurrence)
        => new(
            CodeSmellKind.AsyncVoidMethod,
            SmellSeverity.Warning,
            typeName,
            occurrence.MethodName,
            "async void method (fails silently: exceptions go to the log; callers cannot await or observe failure)",
            occurrence.Line);

    static DetectedSmell ToBlockingWaitSmell(string typeName, BlockingWaitOccurrence occurrence)
        => new(
            CodeSmellKind.BlockingTaskWait,
            SmellSeverity.Warning,
            typeName,
            occurrence.MethodName,
            $"blocking wait via {occurrence.Pattern} (can stall the frame / deadlock on the main thread)",
            occurrence.Line);
}
