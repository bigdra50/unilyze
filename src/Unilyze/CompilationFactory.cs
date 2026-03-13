using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze;

public sealed record CompilationResult(
    CSharpCompilation? Compilation,
    AnalysisLevel Level);

public static class CompilationFactory
{
    public static CompilationResult Create(
        string projectRoot,
        IReadOnlyList<SyntaxTree> syntaxTrees)
    {
        var resolved = UnityDllResolver.Resolve(projectRoot);

        if (resolved.Level == AnalysisLevel.SyntaxOnly || resolved.Paths.Count == 0)
            return new CompilationResult(null, AnalysisLevel.SyntaxOnly);

        var references = new List<MetadataReference>();
        foreach (var path in resolved.Paths)
        {
            try
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            catch
            {
                // Skip unreadable DLLs
            }
        }

        if (references.Count == 0)
            return new CompilationResult(null, AnalysisLevel.SyntaxOnly);

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithNullableContextOptions(NullableContextOptions.Enable);

        var compilation = CSharpCompilation.Create(
            "UnilyzeAnalysis",
            syntaxTrees: syntaxTrees,
            references: references,
            options: options);

        return new CompilationResult(compilation, resolved.Level);
    }
}
