using Unilyze.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Unilyze.Incremental;

// Records, for a re-enriched type T, the set of in-source TypeIds that T's enrichment actually
// resolved (design doc §4.1: UsedTypes(T)). Phase A2 inverts this into RDeps(B) = {T | B ∈
// UsedTypes(T)} for precise invalidation. Phase A1 only RECORDS this set — invalidation is
// unchanged (StructuralChangeDetector's full-fallback still governs every decision).
//
// Deliberately NOT instrumented per syntax kind: the design's LLM debate (§9) found syntax-site
// hooks unfixably non-exhaustive (operators, enumerator/awaiter/deconstruct patterns, initializer
// Add, argument types under conversion capture). Instead this runs ONE IOperation walk per member
// body and records every bound symbol's containing type, plus the declaration-side surfaces
// (base/interfaces/members/attributes/constraints) and the file's using-static/alias targets.
internal static class UsageRecorder
{
    public static IReadOnlyList<string> Record(
        TypeDeclarationSyntax typeDecl,
        SemanticModel model,
        IReadOnlyDictionary<string, string> assemblyByFilePath)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var selfSymbol = model.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;

        if (selfSymbol is not null)
            RecordDeclarationSurface(selfSymbol, assemblyByFilePath, used);

        RecordFileUsingTargets(typeDecl.SyntaxTree, model, assemblyByFilePath, used);
        RecordOperationSurface(typeDecl, model, assemblyByFilePath, used);

        return used.OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    // ---- Declaration-side surfaces (§4.1): the full DIT base-chain walk (every ancestor, not
    // just the immediate base — a receiver typed as this type can bind to any inherited member),
    // interface list, member signature types (incl. generic constraints), and attribute types. ----
    static void RecordDeclarationSurface(
        INamedTypeSymbol selfSymbol,
        IReadOnlyDictionary<string, string> assemblyByFilePath,
        HashSet<string> used)
    {
        var current = selfSymbol.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            CollectType(current, assemblyByFilePath, used);
            current = current.BaseType;
        }

        foreach (var iface in selfSymbol.AllInterfaces)
            CollectType(iface, assemblyByFilePath, used);

        foreach (var attr in selfSymbol.GetAttributes())
            CollectType(attr.AttributeClass, assemblyByFilePath, used);

        foreach (var typeParam in selfSymbol.TypeParameters)
            foreach (var constraint in typeParam.ConstraintTypes)
                CollectType(constraint, assemblyByFilePath, used);

        foreach (var member in selfSymbol.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
                continue;

            foreach (var attr in member.GetAttributes())
                CollectType(attr.AttributeClass, assemblyByFilePath, used);

            switch (member)
            {
                case IMethodSymbol method:
                    CollectType(method.ReturnType, assemblyByFilePath, used);
                    foreach (var p in method.Parameters)
                        CollectType(p.Type, assemblyByFilePath, used);
                    foreach (var tp in method.TypeParameters)
                        foreach (var c in tp.ConstraintTypes)
                            CollectType(c, assemblyByFilePath, used);
                    break;
                case IPropertySymbol prop:
                    CollectType(prop.Type, assemblyByFilePath, used);
                    foreach (var p in prop.Parameters)
                        CollectType(p.Type, assemblyByFilePath, used);
                    break;
                case IFieldSymbol field:
                    CollectType(field.Type, assemblyByFilePath, used);
                    break;
                case IEventSymbol ev:
                    CollectType(ev.Type, assemblyByFilePath, used);
                    break;
            }
        }
    }

    // ---- Per-file resolution environment (§4.1 point 4): using-static / alias targets flow into
    // every type declared in that file. Plain `using N;` namespace imports are out of scope. ----
    static void RecordFileUsingTargets(
        SyntaxTree tree,
        SemanticModel model,
        IReadOnlyDictionary<string, string> assemblyByFilePath,
        HashSet<string> used)
    {
        if (tree.GetRoot() is not CompilationUnitSyntax root)
            return;

        foreach (var directive in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (directive.Alias is not null)
            {
                if (model.GetDeclaredSymbol(directive) is IAliasSymbol { Target: INamedTypeSymbol aliasTarget })
                    CollectType(aliasTarget, assemblyByFilePath, used);
                continue;
            }

            if (directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) && directive.Name is { } name
                && model.GetSymbolInfo(name).Symbol is INamedTypeSymbol staticTarget)
                CollectType(staticTarget, assemblyByFilePath, used);
        }
    }

    // ---- Operation surface (§4.1 points 1-2): one IOperation walk per member body. ----
    static void RecordOperationSurface(
        TypeDeclarationSyntax typeDecl,
        SemanticModel model,
        IReadOnlyDictionary<string, string> assemblyByFilePath,
        HashSet<string> used)
    {
        var operationRoots = MemberBodyEnumerator.Enumerate(typeDecl)
            .SelectMany(member => ResolveOperationRoots(member.ScanRoot));

        foreach (var opRoot in operationRoots)
        {
            var operation = TryGetOperation(model, opRoot);
            if (operation is null)
                continue;

            foreach (var op in WalkOperations(operation))
                RecordFromOperation(op, model, assemblyByFilePath, used);
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

    static void RecordFromOperation(
        IOperation op,
        SemanticModel model,
        IReadOnlyDictionary<string, string> assemblyByFilePath,
        HashSet<string> used)
    {
        CollectType(op.Type, assemblyByFilePath, used);

        switch (op)
        {
            case IConversionOperation { OperatorMethod: { } convOp }:
                CollectType(convOp.ContainingType, assemblyByFilePath, used);
                break;
            case IInvocationOperation invocation:
                CollectType(invocation.TargetMethod?.ContainingType, assemblyByFilePath, used);
                break;
            case IMemberReferenceOperation memberRef:
                CollectType(memberRef.Member?.ContainingType, assemblyByFilePath, used);
                break;
            case IObjectCreationOperation creation:
                CollectType(creation.Constructor?.ContainingType, assemblyByFilePath, used);
                break;
            case IUnaryOperation { OperatorMethod: { } unaryOp }:
                CollectType(unaryOp.ContainingType, assemblyByFilePath, used);
                break;
            case IBinaryOperation { OperatorMethod: { } binaryOp }:
                CollectType(binaryOp.ContainingType, assemblyByFilePath, used);
                break;
            // Collection-initializer `Add(...)` calls surface as a plain IInvocationOperation
            // (the older ICollectionElementInitializerOperation is obsolete) — already covered
            // by the IInvocationOperation case above.
            case IForEachLoopOperation forEach:
                RecordForEachPattern(forEach, model, assemblyByFilePath, used);
                break;
            case IAwaitOperation awaitOp:
                RecordAwaitPattern(awaitOp, model, assemblyByFilePath, used);
                break;
            // A custom (possibly extension) Deconstruct method never appears as a child
            // IInvocationOperation, only in the deconstruction binding info — without this a
            // `var (x, y) = p;` over an extension Deconstruct misses the extension class.
            case IDeconstructionAssignmentOperation { Syntax: AssignmentExpressionSyntax deconstructSyntax }:
                RecordDeconstructionInfo(model.GetDeconstructionInfo(deconstructSyntax), assemblyByFilePath, used);
                break;
        }
    }

    static void RecordDeconstructionInfo(
        DeconstructionInfo info,
        IReadOnlyDictionary<string, string> assemblyByFilePath,
        HashSet<string> used)
    {
        if (info.Method is { } method)
            CollectType(method.ContainingType, assemblyByFilePath, used);
        foreach (var nested in info.Nested)
            RecordDeconstructionInfo(nested, assemblyByFilePath, used);
    }

    static void RecordForEachPattern(
        IForEachLoopOperation forEach,
        SemanticModel model,
        IReadOnlyDictionary<string, string> assemblyByFilePath,
        HashSet<string> used)
    {
        // CommonForEachStatementSyntax also covers `foreach (var (a, b) in ...)`
        // (ForEachVariableStatementSyntax) — matching only ForEachStatementSyntax would drop the
        // enumerator pattern (and the Deconstruct binding) for deconstructing loops.
        if (forEach.Syntax is not CommonForEachStatementSyntax syntax)
            return;

        var info = model.GetForEachStatementInfo(syntax);
        CollectType(info.GetEnumeratorMethod?.ContainingType, assemblyByFilePath, used);
        CollectType(info.MoveNextMethod?.ContainingType, assemblyByFilePath, used);
        CollectType(info.CurrentProperty?.ContainingType, assemblyByFilePath, used);
        CollectType(info.DisposeMethod?.ContainingType, assemblyByFilePath, used);
        CollectType(info.ElementType, assemblyByFilePath, used);

        if (syntax is ForEachVariableStatementSyntax variableSyntax)
            RecordDeconstructionInfo(model.GetDeconstructionInfo(variableSyntax), assemblyByFilePath, used);
    }

    static void RecordAwaitPattern(
        IAwaitOperation awaitOp,
        SemanticModel model,
        IReadOnlyDictionary<string, string> assemblyByFilePath,
        HashSet<string> used)
    {
        if (awaitOp.Syntax is not AwaitExpressionSyntax syntax)
            return;

        var info = model.GetAwaitExpressionInfo(syntax);
        CollectType(info.GetAwaiterMethod?.ContainingType, assemblyByFilePath, used);
        CollectType(info.IsCompletedProperty?.ContainingType, assemblyByFilePath, used);
        CollectType(info.GetResultMethod?.ContainingType, assemblyByFilePath, used);
    }

    // Unwraps arrays/pointers/generic type arguments to record every named type reachable from
    // `type` (mirrors CboCalculator.CollectNamedTypes).
    static void CollectType(
        ITypeSymbol? type,
        IReadOnlyDictionary<string, string> assemblyByFilePath,
        HashSet<string> used)
    {
        switch (type)
        {
            case INamedTypeSymbol named:
                var typeId = TryResolveTypeId(named, assemblyByFilePath);
                if (typeId is not null)
                    used.Add(typeId);
                foreach (var arg in named.TypeArguments)
                    CollectType(arg, assemblyByFilePath, used);
                break;
            case IArrayTypeSymbol array:
                CollectType(array.ElementType, assemblyByFilePath, used);
                break;
            case IPointerTypeSymbol pointer:
                CollectType(pointer.PointedAtType, assemblyByFilePath, used);
                break;
        }
    }

    // Symbol → TypeId mapping (§4.1): a symbol with no in-source declaration is a metadata
    // reference — ignored, since it cannot change mid-session (reference-set/TFM changes already
    // flip the global fingerprint → full rebuild). Partial types: every declaring reference
    // resolves to the same TypeId because TypeIdentity.CreateTypeId is name/arity/namespace/
    // assembly-derived, not file-position-derived, so the first resolvable fragment suffices.
    internal static string? TryResolveTypeId(
        INamedTypeSymbol symbol,
        IReadOnlyDictionary<string, string> assemblyByFilePath)
    {
        var definition = symbol.OriginalDefinition;
        foreach (var declRef in definition.DeclaringSyntaxReferences)
        {
            var tree = declRef.SyntaxTree;
            if (string.IsNullOrEmpty(tree.FilePath))
                continue;
            if (!assemblyByFilePath.TryGetValue(Path.GetFullPath(tree.FilePath), out var assembly))
                continue;

            var typeId = declRef.GetSyntax() switch
            {
                TypeDeclarationSyntax td => TypeIdentity.CreateTypeId(td, assembly),
                EnumDeclarationSyntax ed => TypeIdentity.CreateTypeId(ed, assembly),
                DelegateDeclarationSyntax dd => TypeIdentity.CreateTypeId(dd, assembly),
                _ => null
            };
            if (typeId is not null)
                return typeId;
        }

        return null;
    }
}
