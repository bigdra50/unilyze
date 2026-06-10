using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public sealed class ExceptionFlowSmellDetector : ISmellDetector
{
    public IReadOnlyList<DetectedSmell> Detect(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        var typeName = SmellDetectorHelpers.GetTypeName(typeDecl);
        var flow = ExceptionFlowAnalyzer.Analyze(typeDecl, model);
        var smells = new List<DetectedSmell>();

        foreach (var ca in flow.CatchAllClauses.Where(c => !c.HasRethrow))
        {
            smells.Add(new DetectedSmell(
                CodeSmellKind.CatchAllException,
                SmellSeverity.Warning,
                typeName,
                ca.MethodName,
                $"catch-all at line {ca.Line}",
                ca.Line));
        }

        foreach (var mi in flow.MissingInnerExceptions)
        {
            smells.Add(new DetectedSmell(
                CodeSmellKind.MissingInnerException,
                SmellSeverity.Warning,
                typeName,
                mi.MethodName,
                $"throw new {mi.NewExceptionType} without inner exception",
                mi.Line));
        }

        foreach (var se in flow.SystemExceptionThrows)
        {
            smells.Add(new DetectedSmell(
                CodeSmellKind.ThrowingSystemException,
                SmellSeverity.Warning,
                typeName,
                se.MethodName,
                "throw new Exception() directly",
                se.Line));
        }

        return smells;
    }
}
