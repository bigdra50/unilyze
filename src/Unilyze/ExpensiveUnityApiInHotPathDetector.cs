using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public sealed class ExpensiveUnityApiInHotPathDetector : ISmellDetector
{
    static readonly HashSet<string> ExpensiveUnityApiNames = new(StringComparer.Ordinal)
    {
        "GetComponent", "GetComponents", "GetComponentInChildren", "GetComponentsInChildren",
        "GetComponentInParent", "GetComponentsInParent", "Find", "FindObjectOfType",
        "FindObjectsOfType", "FindFirstObjectByType", "FindAnyObjectByType", "FindObjectsByType",
        "FindGameObjectWithTag", "FindGameObjectsWithTag", "FindWithTag",
    };

    public IReadOnlyList<DetectedSmell> Detect(TypeDeclarationSyntax typeDecl, SemanticModel? model) =>
        UnityHotPathScanHelpers.Scan(typeDecl, model, ScanMethod);

    static void ScanMethod(UnityHotPathScanHelpers.HotPathMethodScan scan)
    {
        foreach (var node in scan.ScanRoot.DescendantNodes())
        {
            if (TryDetectInvocation(node, scan))
                continue;
            TryDetectCameraMain(node, scan);
        }
    }

    static bool TryDetectInvocation(
        SyntaxNode node,
        UnityHotPathScanHelpers.HotPathMethodScan scan)
    {
        if (node is not InvocationExpressionSyntax invocation)
            return false;

        var memberName = GetInvokedMemberName(invocation);
        if (!IsExpensiveUnityApiCall(memberName, invocation, scan.Model))
            return false;

        ReportExpensiveApi(scan, memberName!, invocation);
        return true;
    }

    static void ReportExpensiveApi(
        UnityHotPathScanHelpers.HotPathMethodScan scan,
        string memberName,
        InvocationExpressionSyntax invocation)
    {
        scan.Smells.Add(UnityHotPathScanHelpers.CreateSmell(
            CodeSmellKind.ExpensiveUnityApiInHotPath,
            scan.TypeName,
            scan.MethodName,
            $"{memberName} call in hot-path method '{scan.MethodName}'",
            invocation));
    }

    static bool IsExpensiveUnityApiCall(
        string? memberName,
        InvocationExpressionSyntax invocation,
        SemanticModel? model) =>
        memberName is not null
        && ExpensiveUnityApiNames.Contains(memberName)
        && IsUnityEngineInvocation(invocation, model);

    static void TryDetectCameraMain(
        SyntaxNode node,
        UnityHotPathScanHelpers.HotPathMethodScan scan)
    {
        if (node is not MemberAccessExpressionSyntax { Name.Identifier.Text: "main" } access)
            return;
        if (!IsCameraMain(access, scan.Model))
            return;

        scan.Smells.Add(UnityHotPathScanHelpers.CreateSmell(
            CodeSmellKind.ExpensiveUnityApiInHotPath,
            scan.TypeName,
            scan.MethodName,
            $"Camera.main access in hot-path method '{scan.MethodName}'",
            access));
    }

    static string? GetInvokedMemberName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            GenericNameSyntax gen => gen.Identifier.Text,
            _ => null,
        };

    static bool IsUnityEngineInvocation(InvocationExpressionSyntax invocation, SemanticModel? model)
    {
        if (model is null)
            return true;

        var symbol = model.GetSymbolInfo(invocation).Symbol;
        if (symbol is null)
            return true;

        return UnityHotPathScanHelpers.IsUnityEngineNamespace(symbol.ContainingType?.ContainingNamespace);
    }

    static bool IsCameraMain(MemberAccessExpressionSyntax access, SemanticModel? model)
    {
        if (!access.Expression.ToString().EndsWith("Camera", StringComparison.Ordinal))
            return false;

        if (model is null)
            return true;

        return IsUnityCameraType(model.GetTypeInfo(access).Type);
    }

    static bool IsUnityCameraType(ITypeSymbol? type) =>
        type is not null
        && type.Name == "Camera"
        && UnityHotPathScanHelpers.IsUnityEngineNamespace(type.ContainingNamespace);
}
