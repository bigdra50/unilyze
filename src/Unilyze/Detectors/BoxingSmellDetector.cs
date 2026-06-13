using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Detectors;

internal sealed class BoxingSmellDetector : ISmellDetector
{
    public IReadOnlyList<DetectedSmell> Detect(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        var typeName = SmellDetectorHelpers.GetTypeName(typeDecl);
        return BoxingDetector.Detect(typeDecl, model)
            .Select(b => new DetectedSmell(
                CodeSmellKind.BoxingAllocation,
                SmellSeverity.Warning,
                typeName,
                b.MethodName,
                b.Description,
                b.Line))
            .ToList();
    }
}
