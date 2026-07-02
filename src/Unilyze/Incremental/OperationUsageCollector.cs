using Unilyze.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Unilyze.Incremental;

// Operation surface of UsedTypes(T) (design doc §4.1 points 1-2): one IOperation walk per member
// body. Deliberately NOT instrumented per syntax kind — the design's LLM debate (§9) found
// syntax-site hooks unfixably non-exhaustive (operators, enumerator/awaiter/deconstruct patterns,
// initializer Add, argument types under conversion capture). Every operation contributes its
// Type/ConvertedType (conversions are explicit IConversionOperation nodes, so ConvertedType is
// structural) and every bound symbol contributes its containing type.
internal static class OperationUsageCollector
{
    public static void Collect(
        TypeDeclarationSyntax typeDecl, SemanticModel model, UsedTypeCollection used)
    {
        var operationRoots = MemberBodyEnumerator.Enumerate(typeDecl)
            .SelectMany(member => ResolveOperationRoots(member.ScanRoot));

        foreach (var opRoot in operationRoots)
        {
            var operation = TryGetOperation(model, opRoot);
            if (operation is null)
                continue;

            foreach (var op in WalkOperations(operation))
                CollectFromOperation(op, model, used);
        }
    }

    static IOperation? TryGetOperation(SemanticModel model, SyntaxNode node)
    {
        try
        {
            return model.GetOperation(node);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException)
        {
            // Roslyn-internal failures on malformed/edge-case trees must not crash a re-enrich —
            // mirrors SemanticEnricher's catch-and-fall-back posture elsewhere in the pipeline.
            return null;
        }
    }

    // Method-like ScanRoots from MemberBodyEnumerator are the WHOLE declaration node (header +
    // body); only the Body/ExpressionBody/(constructor) Initializer are IOperation roots. Field
    // and property initializer ScanRoots are already the initializer expression itself.
    static IEnumerable<SyntaxNode> ResolveOperationRoots(SyntaxNode scanRoot)
    {
        switch (scanRoot)
        {
            case MethodDeclarationSyntax m:
                foreach (var r in BodyRoots(m.Body, m.ExpressionBody)) yield return r;
                break;
            case ConstructorDeclarationSyntax c:
                if (c.Initializer is not null) yield return c.Initializer;
                foreach (var r in BodyRoots(c.Body, c.ExpressionBody)) yield return r;
                break;
            case OperatorDeclarationSyntax o:
                foreach (var r in BodyRoots(o.Body, o.ExpressionBody)) yield return r;
                break;
            case ConversionOperatorDeclarationSyntax co:
                foreach (var r in BodyRoots(co.Body, co.ExpressionBody)) yield return r;
                break;
            case AccessorDeclarationSyntax a:
                foreach (var r in BodyRoots(a.Body, a.ExpressionBody)) yield return r;
                break;
            case LocalFunctionStatementSyntax lf:
                foreach (var r in BodyRoots(lf.Body, lf.ExpressionBody)) yield return r;
                break;
            default:
                yield return scanRoot;
                break;
        }
    }

    static IEnumerable<SyntaxNode> BodyRoots(BlockSyntax? body, ArrowExpressionClauseSyntax? exprBody)
    {
        if (body is not null) yield return body;
        else if (exprBody is not null) yield return exprBody.Expression;
    }

    static IEnumerable<IOperation> WalkOperations(IOperation root)
    {
        var stack = new Stack<IOperation>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var op = stack.Pop();
            yield return op;
            foreach (var child in op.ChildOperations)
                stack.Push(child);
        }
    }

    static void CollectFromOperation(IOperation op, SemanticModel model, UsedTypeCollection used)
    {
        used.Add(op.Type);

        switch (op)
        {
            case IConversionOperation { OperatorMethod: { } convOp }:
                used.AddContaining(convOp);
                break;
            case IInvocationOperation invocation:
                used.AddContaining(invocation.TargetMethod);
                break;
            case IMemberReferenceOperation memberRef:
                used.AddContaining(memberRef.Member);
                break;
            case IObjectCreationOperation creation:
                used.AddContaining(creation.Constructor);
                break;
            case IUnaryOperation { OperatorMethod: { } unaryOp }:
                used.AddContaining(unaryOp);
                break;
            case IBinaryOperation { OperatorMethod: { } binaryOp }:
                used.AddContaining(binaryOp);
                break;
            // Collection-initializer `Add(...)` calls surface as a plain IInvocationOperation
            // (the older ICollectionElementInitializerOperation is obsolete) — already covered
            // by the IInvocationOperation case above.
            case IForEachLoopOperation forEach:
                CollectForEachPattern(forEach, model, used);
                break;
            case IAwaitOperation awaitOp:
                CollectAwaitPattern(awaitOp, model, used);
                break;
            // A custom (possibly extension) Deconstruct method never appears as a child
            // IInvocationOperation, only in the deconstruction binding info — without this a
            // `var (x, y) = p;` over an extension Deconstruct misses the extension class.
            case IDeconstructionAssignmentOperation { Syntax: AssignmentExpressionSyntax deconstructSyntax }:
                CollectDeconstructionInfo(model.GetDeconstructionInfo(deconstructSyntax), used);
                break;
        }
    }

    static void CollectForEachPattern(
        IForEachLoopOperation forEach, SemanticModel model, UsedTypeCollection used)
    {
        // CommonForEachStatementSyntax also covers `foreach (var (a, b) in ...)`
        // (ForEachVariableStatementSyntax) — matching only ForEachStatementSyntax would drop the
        // enumerator pattern (and the Deconstruct binding) for deconstructing loops.
        if (forEach.Syntax is not CommonForEachStatementSyntax syntax)
            return;

        var info = model.GetForEachStatementInfo(syntax);
        used.AddContaining(info.GetEnumeratorMethod);
        used.AddContaining(info.MoveNextMethod);
        used.AddContaining(info.CurrentProperty);
        used.AddContaining(info.DisposeMethod);
        used.Add(info.ElementType);

        if (syntax is ForEachVariableStatementSyntax variableSyntax)
            CollectDeconstructionInfo(model.GetDeconstructionInfo(variableSyntax), used);
    }

    static void CollectAwaitPattern(
        IAwaitOperation awaitOp, SemanticModel model, UsedTypeCollection used)
    {
        if (awaitOp.Syntax is not AwaitExpressionSyntax syntax)
            return;

        var info = model.GetAwaitExpressionInfo(syntax);
        used.AddContaining(info.GetAwaiterMethod);
        used.AddContaining(info.IsCompletedProperty);
        used.AddContaining(info.GetResultMethod);
    }

    static void CollectDeconstructionInfo(DeconstructionInfo info, UsedTypeCollection used)
    {
        if (info.Method is { } method)
            used.AddContaining(method);
        foreach (var nested in info.Nested)
            CollectDeconstructionInfo(nested, used);
    }
}
