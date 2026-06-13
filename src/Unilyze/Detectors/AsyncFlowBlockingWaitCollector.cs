namespace Unilyze.Detectors;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class AsyncFlowBlockingWaitCollector
{
    sealed record BlockingWaitReportContext(
        ExpressionSyntax Receiver,
        SyntaxNode LocationNode,
        string Pattern,
        List<BlockingWaitOccurrence> Results,
        SemanticModel? Model,
        bool AllowSyntaxOnly = false);

    public static List<BlockingWaitOccurrence> Collect(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        var blockingWaits = new List<BlockingWaitOccurrence>();
        CollectResultPropertyAccesses(typeDecl, model, blockingWaits);
        CollectInvocations(typeDecl, model, blockingWaits);
        return blockingWaits;
    }

    static void CollectResultPropertyAccesses(
        TypeDeclarationSyntax typeDecl,
        SemanticModel? model,
        List<BlockingWaitOccurrence> results)
    {
        foreach (var memberAccess in typeDecl.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (!IsStandaloneResultAccess(memberAccess))
                continue;

            TryReportBlockingWait(new BlockingWaitReportContext(
                memberAccess.Expression,
                memberAccess,
                "Result",
                results,
                model));
        }
    }

    static bool IsStandaloneResultAccess(MemberAccessExpressionSyntax memberAccess)
    {
        if (memberAccess.Name.Identifier.Text != "Result")
            return false;

        return memberAccess.Parent is not InvocationExpressionSyntax inv || inv.Expression != memberAccess;
    }

    static void CollectInvocations(
        TypeDeclarationSyntax typeDecl,
        SemanticModel? model,
        List<BlockingWaitOccurrence> results)
    {
        foreach (var invocation in typeDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (TryCollectWaitInvocation(invocation, model, results))
                continue;

            CollectGetAwaiterGetResultInvocation(invocation, model, results);
        }
    }

    static bool TryCollectWaitInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel? model,
        List<BlockingWaitOccurrence> results)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "Wait" } waitAccess)
            return false;

        TryReportBlockingWait(new BlockingWaitReportContext(
            waitAccess.Expression,
            invocation,
            "Wait",
            results,
            model));
        return true;
    }

    static void CollectGetAwaiterGetResultInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel? model,
        List<BlockingWaitOccurrence> results)
    {
        if (!TryGetGetAwaiterGetResultChain(invocation, out var receiverBeforeAwaiter))
            return;

        TryReportBlockingWait(new BlockingWaitReportContext(
            receiverBeforeAwaiter,
            invocation,
            "GetAwaiter().GetResult()",
            results,
            model,
            AllowSyntaxOnly: true));
    }

    static void TryReportBlockingWait(BlockingWaitReportContext ctx)
    {
        if (!ShouldReportBlockingWait(ctx))
            return;

        var methodName = AsyncFlowEnclosingMemberName.Get(ctx.LocationNode);
        var line = ctx.LocationNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        ctx.Results.Add(new BlockingWaitOccurrence(methodName, line, ctx.Pattern));
    }

    static bool ShouldReportBlockingWait(BlockingWaitReportContext ctx)
    {
        if (ctx.Model is not null)
        {
            var typeInfo = ctx.Model.GetTypeInfo(ctx.Receiver).Type;
            if (typeInfo is null)
                return ctx.AllowSyntaxOnly;

            return AsyncFlowTaskLikeTypes.IsTaskLike(typeInfo);
        }

        return ctx.AllowSyntaxOnly;
    }

    static bool TryGetGetAwaiterGetResultChain(
        InvocationExpressionSyntax invocation,
        out ExpressionSyntax receiverBeforeAwaiter)
    {
        receiverBeforeAwaiter = null!;

        if (!TryGetGetResultAccess(invocation, out var getResultAccess))
            return false;

        if (!TryGetGetAwaiterAccess(getResultAccess, out var getAwaiterAccess))
            return false;

        receiverBeforeAwaiter = getAwaiterAccess.Expression;
        return true;
    }

    static bool TryGetGetResultAccess(
        InvocationExpressionSyntax invocation,
        out MemberAccessExpressionSyntax getResultAccess)
    {
        getResultAccess = null!;
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "GetResult" } access)
            return false;

        getResultAccess = access;
        return true;
    }

    static bool TryGetGetAwaiterAccess(
        MemberAccessExpressionSyntax getResultAccess,
        out MemberAccessExpressionSyntax getAwaiterAccess)
    {
        getAwaiterAccess = null!;
        if (getResultAccess.Expression is not InvocationExpressionSyntax getAwaiterInvocation)
            return false;

        if (getAwaiterInvocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "GetAwaiter" } access)
            return false;

        getAwaiterAccess = access;
        return true;
    }
}
