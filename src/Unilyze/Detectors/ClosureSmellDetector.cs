using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Detectors;

internal sealed class ClosureSmellDetector : ISmellDetector
{
    public IReadOnlyList<DetectedSmell> Detect(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        var typeName = SmellDetectorHelpers.GetTypeName(typeDecl);
        return ClosureDetector.Detect(typeDecl, model)
            .Select(c => new DetectedSmell(
                CodeSmellKind.ClosureCapture,
                SmellSeverity.Warning,
                typeName,
                c.MethodName,
                $"captures: {string.Join(", ", c.CapturedVariables)}",
                c.Line))
            .ToList();
    }
}
