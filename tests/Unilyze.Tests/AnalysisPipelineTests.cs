using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze.Tests;

public sealed class AnalysisPipelineTests : IDisposable
{
    readonly string _tempDir;

    public AnalysisPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Unilyze_AnalysisPipelineTests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [Fact]
    public void ResolveTypeRelationships_UsesSemanticModelToKeepClassLikeIBuilderAsBaseType()
    {
        WriteFile("Types.cs", """
            namespace Sample;

            public class IBuilder { }
            public interface IService { }

            public class MyBuilder : IBuilder, IService { }
            """);

        var analyzed = TypeAnalyzer.AnalyzeDirectoryWithTrees(_tempDir, "Asm");
        var compilation = CSharpCompilation.Create(
            "Test",
            analyzed.SyntaxTrees,
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var resolved = AnalysisPipeline.ResolveTypeRelationships(
            analyzed.Types,
            analyzed.SyntaxTrees,
            new CompilationResult(compilation, AnalysisLevel.CoreEngine));

        var myBuilder = resolved.Single(t => t.Name == "MyBuilder");
        Assert.Equal("IBuilder", myBuilder.BaseType);
        Assert.Equal(["IService"], myBuilder.Interfaces);
    }
}
