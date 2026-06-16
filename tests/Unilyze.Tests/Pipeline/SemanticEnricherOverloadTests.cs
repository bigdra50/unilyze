using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze.Tests.Pipeline;

// Regression guard: the semantic CycCC recalculation must bind each overload to its OWN
// declaration. Keying the method lookup by bare name made every overload pick up the first
// declaration's body, collapsing a complex overload's cyclomatic complexity to the simple one's.
public sealed class SemanticEnricherOverloadTests : IDisposable
{
    readonly string _tempDir;

    public SemanticEnricherOverloadTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-overload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Enrich_Overloads_EachKeepsItsOwnCyclomaticComplexity()
    {
        // The simple overload is declared first, so a name-keyed lookup would bind the complex
        // overload to it and recompute the complex method's CycCC as 1.
        WriteSources("""
            namespace Sample;

            public class Overloaded
            {
                public int Calc() => 1;

                public int Calc(int a, int b, int c, int d)
                {
                    var n = 0;
                    if (a > 0) n++;
                    if (b > 0) n++;
                    if (c > 0) n++;
                    if (d > 0) n++;
                    return n;
                }
            }
            """);

        var metrics = EnrichFromDisk();
        var methods = metrics.Single(m => m.TypeName == "Overloaded").Methods;

        var simple = methods.Single(m => m.MethodName == "Calc" && m.ParameterCount == 0);
        var complex = methods.Single(m => m.MethodName == "Calc" && m.ParameterCount == 4);

        Assert.Equal(1, simple.CyclomaticComplexity);
        Assert.Equal(5, complex.CyclomaticComplexity); // base 1 + four branches
    }

    IReadOnlyList<TypeMetrics> EnrichFromDisk()
    {
        var analyzed = TypeAnalyzer.AnalyzeDirectoryWithTrees(_tempDir, "TestAsm");
        var compilation = CSharpCompilation.Create(
            "TestAsm",
            analyzed.SyntaxTrees,
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var compilationResult = new CompilationResult(compilation, AnalysisLevel.Core);
        var allTypes = BaseTypeResolver
            .ResolveTypeRelationships(analyzed.Types, analyzed.SyntaxTrees, compilationResult)
            .ToList();
        var typeMetrics = CodeHealthCalculator.ComputeTypeMetrics(allTypes);

        return SemanticEnricher.Enrich(
            typeMetrics, allTypes, analyzed.SyntaxTrees, compilationResult);
    }

    void WriteSources(string code) =>
        File.WriteAllText(Path.Combine(_tempDir, "Types.cs"), code);
}
