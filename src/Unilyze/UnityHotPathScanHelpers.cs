using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

internal static class UnityHotPathScanHelpers
{
    internal sealed record HotPathMethodScan(
        SyntaxNode ScanRoot,
        string TypeName,
        string MethodName,
        SemanticModel? Model,
        List<DetectedSmell> Smells);

    sealed record HotPathTypeScan(
        TypeDeclarationSyntax TypeDecl,
        IReadOnlySet<string> HotPathMethodNames,
        string TypeName,
        SemanticModel? Model,
        List<DetectedSmell> Smells,
        Action<HotPathMethodScan> ScanMethod);

    internal static IReadOnlyList<DetectedSmell> Scan(
        TypeDeclarationSyntax typeDecl,
        SemanticModel? model,
        Action<HotPathMethodScan> scanMethod)
    {
        var context = UnityContextClassifier.Classify(typeDecl, model);
        if (!context.IsMonoBehaviour)
            return [];

        var typeName = SmellDetectorHelpers.GetTypeName(typeDecl);
        var smells = new List<DetectedSmell>();
        ScanHotPathMembers(new HotPathTypeScan(
            typeDecl, context.HotPathMethodNames, typeName, model, smells, scanMethod));
        return smells;
    }

    static void ScanHotPathMembers(HotPathTypeScan request)
    {
        foreach (var member in request.TypeDecl.Members.OfType<MethodDeclarationSyntax>())
            ScanHotPathMember(request, member);
    }

    static void ScanHotPathMember(HotPathTypeScan request, MethodDeclarationSyntax member)
    {
        if (!request.HotPathMethodNames.Contains(member.Identifier.Text))
            return;

        var scanRoot = (SyntaxNode?)member.Body ?? member.ExpressionBody;
        if (scanRoot is null)
            return;

        request.ScanMethod(new HotPathMethodScan(
            scanRoot, request.TypeName, member.Identifier.Text, request.Model, request.Smells));
    }

    internal static DetectedSmell CreateSmell(
        CodeSmellKind kind,
        string typeName,
        string methodName,
        string message,
        SyntaxNode node) =>
        new(
            kind,
            SmellSeverity.Warning,
            typeName,
            methodName,
            message,
            node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);

    internal static string? GetLastIdentifierSegment(TypeSyntax typeSyntax) =>
        typeSyntax switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax qual => qual.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            GenericNameSyntax gen => gen.Identifier.Text,
            _ => null,
        };

    internal static bool IsUnityEngineNamespace(INamespaceSymbol? ns)
    {
        if (ns is null)
            return false;

        var display = ns.ToDisplayString();
        return display is "UnityEngine" or "global::UnityEngine"
            || display.StartsWith("UnityEngine.", StringComparison.Ordinal);
    }
}
