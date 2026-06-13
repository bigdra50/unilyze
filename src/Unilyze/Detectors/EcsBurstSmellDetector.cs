using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Detectors;

internal sealed class EcsBurstSmellDetector : ISmellDetector
{
    public IReadOnlyList<DetectedSmell> Detect(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (!EcsBurstCompileChecker.IsMissingBurstCompile(typeDecl, model))
            return [];

        var typeName = SmellDetectorHelpers.GetTypeName(typeDecl);
        var line = typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        return
        [
            new DetectedSmell(
                CodeSmellKind.MissingBurstCompile,
                SmellSeverity.Warning,
                typeName,
                null,
                "Burst-eligible ECS type is not Burst-compiled; add [BurstCompile] on the type or lifecycle methods, or disable UNI024 via rules if intentional",
                line)
        ];
    }
}
