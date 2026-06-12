using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public sealed record DetectedSmell(
    CodeSmellKind Kind,
    SmellSeverity Severity,
    string TypeName,
    string? MethodName,
    string Message,
    int? Line,
    bool? InHotPath = null);

public interface ISmellDetector
{
    IReadOnlyList<DetectedSmell> Detect(TypeDeclarationSyntax typeDecl, SemanticModel? model);
}

internal static class SmellDetectorHelpers
{
    internal static string GetTypeName(TypeDeclarationSyntax typeDecl)
    {
        var name = typeDecl.Identifier.Text;
        if (typeDecl.TypeParameterList is { } tpl)
            name += $"<{string.Join(",", tpl.Parameters.Select(p => p.Identifier.Text))}>";
        return name;
    }
}
