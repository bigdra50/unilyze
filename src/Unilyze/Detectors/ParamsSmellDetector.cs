using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Detectors;

internal sealed class ParamsSmellDetector : ISmellDetector
{
    public IReadOnlyList<DetectedSmell> Detect(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        var typeName = SmellDetectorHelpers.GetTypeName(typeDecl);
        return ParamsArrayDetector.Detect(typeDecl, model)
            .Select(p => new DetectedSmell(
                CodeSmellKind.ParamsArrayAllocation,
                SmellSeverity.Warning,
                typeName,
                p.MethodName,
                $"params call to {p.CalledMethod} ({p.ArgCount} args)",
                p.Line))
            .ToList();
    }
}
