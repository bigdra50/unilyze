using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public sealed class LinqInHotPathDetector : ISmellDetector
{
    static readonly HashSet<string> LinqOperatorNames = new(StringComparer.Ordinal)
    {
        "Where", "Select", "SelectMany", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
        "GroupBy", "ToList", "ToArray", "ToDictionary", "ToHashSet", "First", "FirstOrDefault",
        "Last", "LastOrDefault", "Single", "SingleOrDefault", "Any", "All", "Count", "Sum", "Min",
        "Max", "Average", "Aggregate", "Distinct", "Concat", "Union", "Intersect", "Except", "Zip",
        "Skip", "Take", "Reverse", "Contains",
    };

    public IReadOnlyList<DetectedSmell> Detect(TypeDeclarationSyntax typeDecl, SemanticModel? model) =>
        UnityHotPathScanHelpers.Scan(typeDecl, model, ScanMethod);

    static void ScanMethod(UnityHotPathScanHelpers.HotPathMethodScan scan)
    {
        foreach (var node in scan.ScanRoot.DescendantNodes())
            TryDetect(node, scan);
    }

    static void TryDetect(SyntaxNode node, UnityHotPathScanHelpers.HotPathMethodScan scan)
    {
        if (TryDetectQuery(node, scan))
            return;
        TryDetectMethodCall(node, scan);
    }

    static bool TryDetectQuery(SyntaxNode node, UnityHotPathScanHelpers.HotPathMethodScan scan)
    {
        if (node is not QueryExpressionSyntax query)
            return false;

        scan.Smells.Add(UnityHotPathScanHelpers.CreateSmell(
            CodeSmellKind.LinqInHotPath,
            scan.TypeName,
            scan.MethodName,
            $"LINQ query in hot-path method '{scan.MethodName}'",
            query));
        return true;
    }

    sealed record LinqInvocationMatch(InvocationExpressionSyntax Invocation, string OpName);

    static void TryDetectMethodCall(SyntaxNode node, UnityHotPathScanHelpers.HotPathMethodScan scan)
    {
        var match = MatchLinqInvocation(node, scan.Model);
        if (match is null)
            return;

        ReportLinqCall(scan, match.OpName, match.Invocation);
    }

    static LinqInvocationMatch? MatchLinqInvocation(SyntaxNode node, SemanticModel? model)
    {
        if (node is not InvocationExpressionSyntax candidate)
            return null;
        if (IsDescendantOfQueryExpression(candidate))
            return null;
        if (candidate.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;
        if (!IsLinqInvocation(candidate, memberAccess, model))
            return null;

        return new LinqInvocationMatch(candidate, memberAccess.Name.Identifier.Text);
    }

    static void ReportLinqCall(
        UnityHotPathScanHelpers.HotPathMethodScan scan,
        string opName,
        InvocationExpressionSyntax invocation)
    {
        scan.Smells.Add(UnityHotPathScanHelpers.CreateSmell(
            CodeSmellKind.LinqInHotPath,
            scan.TypeName,
            scan.MethodName,
            $"LINQ '{opName}' in hot-path method '{scan.MethodName}'",
            invocation));
    }

    static bool IsDescendantOfQueryExpression(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is QueryExpressionSyntax)
                return true;
        }

        return false;
    }

    static bool IsLinqInvocation(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel? model)
    {
        if (model is not null)
        {
            var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is not null)
                return IsLinqContainingType(symbol.ContainingType);
        }

        return LinqOperatorNames.Contains(memberAccess.Name.Identifier.Text);
    }

    static bool IsLinqContainingType(INamedTypeSymbol? type)
    {
        if (type is null)
            return false;

        var ns = type.ContainingNamespace?.ToDisplayString();
        if (ns is not "System.Linq" and not "global::System.Linq")
            return false;

        return type.Name is "Enumerable" or "Queryable";
    }
}
