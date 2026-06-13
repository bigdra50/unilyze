using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Detectors;

internal sealed class StringConcatenationInHotPathDetector : ISmellDetector
{
    public IReadOnlyList<DetectedSmell> Detect(TypeDeclarationSyntax typeDecl, SemanticModel? model) =>
        UnityHotPathScanHelpers.Scan(typeDecl, model, ScanMethod);

    static void ScanMethod(UnityHotPathScanHelpers.HotPathMethodScan scan)
    {
        var reportedNodes = new HashSet<SyntaxNode>();
        foreach (var node in scan.ScanRoot.DescendantNodes())
            TryDetect(new StringConcatScanContext(scan, reportedNodes, node));
    }

    sealed record StringConcatScanContext(
        UnityHotPathScanHelpers.HotPathMethodScan Scan,
        HashSet<SyntaxNode> ReportedNodes,
        SyntaxNode Node);

    static void TryDetect(StringConcatScanContext ctx)
    {
        if (TryDetectInterpolated(ctx))
            return;
        if (TryDetectBinaryAdd(ctx))
            return;
        TryDetectStringMethod(ctx);
    }

    static bool TryDetectInterpolated(StringConcatScanContext ctx)
    {
        if (ctx.Node is not InterpolatedStringExpressionSyntax interpolated)
            return false;
        if (!ctx.ReportedNodes.Add(interpolated))
            return true;

        ReportStringConcat(ctx.Scan, interpolated, "String interpolation");
        return true;
    }

    static bool TryDetectBinaryAdd(StringConcatScanContext ctx)
    {
        if (ctx.Node is not BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } binary)
            return false;
        if (ShouldSkipBinaryAdd(binary, ctx.Scan.Model))
            return true;
        if (!ctx.ReportedNodes.Add(binary))
            return true;

        ReportStringConcat(ctx.Scan, binary, "String concatenation");
        return true;
    }

    static bool ShouldSkipBinaryAdd(BinaryExpressionSyntax binary, SemanticModel? model) =>
        binary.Left is InterpolatedStringExpressionSyntax
        || binary.Right is InterpolatedStringExpressionSyntax
        || !IsStringAddition(binary, model);

    static void TryDetectStringMethod(StringConcatScanContext ctx)
    {
        if (ctx.Node is not InvocationExpressionSyntax invocation
            || invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var memberName = memberAccess.Name.Identifier.Text;
        if (!IsStringUtilityCall(memberName, memberAccess, ctx.Scan.Model))
            return;
        if (!ctx.ReportedNodes.Add(invocation))
            return;

        ReportStringConcat(ctx.Scan, invocation, $"string.{memberName}");
    }

    static bool IsStringUtilityCall(
        string memberName,
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel? model) =>
        memberName is "Concat" or "Format" or "Join"
        && IsSystemStringMember(memberAccess, model);

    static void ReportStringConcat(
        UnityHotPathScanHelpers.HotPathMethodScan scan,
        SyntaxNode node,
        string description)
    {
        scan.Smells.Add(UnityHotPathScanHelpers.CreateSmell(
            CodeSmellKind.StringConcatenationInHotPath,
            scan.TypeName,
            scan.MethodName,
            $"{description} in hot-path method '{scan.MethodName}'",
            node));
    }

    static bool IsStringAddition(BinaryExpressionSyntax binary, SemanticModel? model)
    {
        if (model is not null)
            return IsStringAdditionSemantic(binary, model);

        return HasStringLiteralOperand(binary.Left) || HasStringLiteralOperand(binary.Right);
    }

    static bool IsStringAdditionSemantic(BinaryExpressionSyntax binary, SemanticModel model)
    {
        var resultType = model.GetTypeInfo(binary).Type;
        if (resultType?.SpecialType == SpecialType.System_String)
            return true;

        var leftType = model.GetTypeInfo(binary.Left).Type;
        var rightType = model.GetTypeInfo(binary.Right).Type;
        return leftType?.SpecialType == SpecialType.System_String
            || rightType?.SpecialType == SpecialType.System_String;
    }

    static bool HasStringLiteralOperand(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression }
            or InterpolatedStringExpressionSyntax;

    static bool IsSystemStringMember(MemberAccessExpressionSyntax memberAccess, SemanticModel? model)
    {
        if (model is not null)
        {
            var symbol = model.GetSymbolInfo(memberAccess).Symbol;
            return symbol?.ContainingType.SpecialType == SpecialType.System_String;
        }

        return memberAccess.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text is "string" or "String",
            _ => false,
        };
    }
}
