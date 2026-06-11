using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public sealed class ManagedComponentDataSmellDetector : ISmellDetector
{
    public IReadOnlyList<DetectedSmell> Detect(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (!EcsContextClassifier.IsEcsComponentData(typeDecl, model))
            return [];

        var typeName = SmellDetectorHelpers.GetTypeName(typeDecl);
        var smells = new List<DetectedSmell>();

        foreach (var member in typeDecl.Members)
        {
            if (member is not FieldDeclarationSyntax field)
                continue;
            if (!EcsManagedFieldChecker.IsManagedComponentField(field, model))
                continue;

            var fieldLine = field.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var fieldName = field.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "field";
            smells.Add(new DetectedSmell(
                CodeSmellKind.ManagedReferenceInComponentData,
                SmellSeverity.Warning,
                typeName,
                fieldName,
                "IComponentData struct declares a reference-type field; use unmanaged types or class IComponentData for intentional managed components",
                fieldLine));
        }

        return smells;
    }
}
