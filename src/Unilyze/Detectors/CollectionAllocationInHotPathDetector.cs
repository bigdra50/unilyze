using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Detectors;

internal sealed class CollectionAllocationInHotPathDetector : ISmellDetector
{
    static readonly HashSet<string> CollectionTypeNames = new(StringComparer.Ordinal)
    {
        "List", "Dictionary", "HashSet", "SortedList", "SortedDictionary", "SortedSet", "Queue",
        "Stack", "LinkedList", "Collection", "ObservableCollection", "ArrayList", "Hashtable",
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
        if (node is ObjectCreationExpressionSyntax objCreate)
            TryDetectObjectCreation(objCreate, scan);
        else if (node is ArrayCreationExpressionSyntax arrayCreate)
            ReportAllocation(scan, arrayCreate, "Array allocation");
        else if (node is ImplicitArrayCreationExpressionSyntax implicitArray)
            ReportAllocation(scan, implicitArray, "Array allocation");
        else if (node is CollectionExpressionSyntax { Elements.Count: > 0 } collectionExpr)
            ReportAllocation(scan, collectionExpr, "Collection expression allocation");
    }

    static void TryDetectObjectCreation(
        ObjectCreationExpressionSyntax objCreate,
        UnityHotPathScanHelpers.HotPathMethodScan scan)
    {
        var collType = UnityHotPathScanHelpers.GetLastIdentifierSegment(objCreate.Type);
        if (collType is null || !CollectionTypeNames.Contains(collType))
            return;

        ReportAllocation(scan, objCreate, "Collection allocation");
    }

    static void ReportAllocation(
        UnityHotPathScanHelpers.HotPathMethodScan scan,
        SyntaxNode node,
        string allocationKind)
    {
        scan.Smells.Add(UnityHotPathScanHelpers.CreateSmell(
            CodeSmellKind.CollectionAllocationInHotPath,
            scan.TypeName,
            scan.MethodName,
            $"{allocationKind} in hot-path method '{scan.MethodName}'",
            node));
    }
}
