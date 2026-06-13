using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze.Tests.Pipeline;

public sealed class SemanticEnricherFallbackTests : IDisposable
{
    readonly string _tempDir;

    public SemanticEnricherFallbackTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-enricher-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        SemanticEnricher.TestSimulateRoslynFailureInCohesion = null;
        SemanticEnricher.TestSimulateRoslynFailureInFeatureDetect = null;

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Enrich_CohesionCatchPath_FallsBackToSyntacticMetricsWithoutThrowing()
    {
        WriteSources("""
            namespace Sample;

            public class HealthyType
            {
                int fieldA;
                int fieldB;
                public void UseA() { fieldA = 1; }
                public void UseB() { fieldB = 2; }
            }

            public class CohesionCrashType
            {
                int fieldA;
                int fieldB;
                public void UseA() { fieldA = 1; }
                public void UseB() { fieldB = 2; }
            }
            """);

        var baseline = EnrichFromDisk();
        var healthyBaseline = FindMetrics(baseline, "HealthyType");
        var crashBaseline = FindMetrics(baseline, "CohesionCrashType");

        SemanticEnricher.TestSimulateRoslynFailureInCohesion =
            td => td.Identifier.Text == "CohesionCrashType";

        var enriched = EnrichFromDisk();
        var healthyEnriched = FindMetrics(enriched, "HealthyType");
        var crashEnriched = FindMetrics(enriched, "CohesionCrashType");

        AssertHealthyMetricsUnchanged(healthyBaseline, healthyEnriched);
        Assert.NotNull(crashEnriched.Lcom);
        Assert.NotNull(crashEnriched.Cbo);
        Assert.NotNull(crashEnriched.Rfc);
        Assert.NotNull(crashEnriched.Dit);
        Assert.Equal(crashBaseline.Wmc, crashEnriched.Wmc);
        Assert.Equal(crashBaseline.CodeHealth, crashEnriched.CodeHealth);
    }

    [Fact]
    public void Enrich_FeatureDetectorCatchPath_DegradesCountsWhilePreservingBaseMetrics()
    {
        WriteSources("""
            namespace Sample;

            public class HealthyType
            {
                public void Box() { object o = 42; }
            }

            public class DetectorCrashType
            {
                public void Box() { object o = 42; }
            }
            """);

        var baseline = EnrichFromDisk();
        var healthyBaseline = FindMetrics(baseline, "HealthyType");
        var crashBaseline = FindMetrics(baseline, "DetectorCrashType");

        Assert.NotNull(healthyBaseline.BoxingCount);
        Assert.True(healthyBaseline.BoxingCount > 0);
        Assert.NotNull(crashBaseline.BoxingCount);
        Assert.True(crashBaseline.BoxingCount > 0);

        SemanticEnricher.TestSimulateRoslynFailureInFeatureDetect =
            td => td.Identifier.Text == "DetectorCrashType";

        var enriched = EnrichFromDisk();
        var healthyEnriched = FindMetrics(enriched, "HealthyType");
        var crashEnriched = FindMetrics(enriched, "DetectorCrashType");

        AssertHealthyMetricsUnchanged(healthyBaseline, healthyEnriched);
        Assert.Null(crashEnriched.BoxingCount);
        Assert.Null(crashEnriched.ClosureCaptureCount);
        Assert.Null(crashEnriched.ParamsAllocationCount);
        Assert.NotNull(crashEnriched.Wmc);
        Assert.Equal(crashBaseline.Wmc, crashEnriched.Wmc);
        Assert.Equal(crashBaseline.CodeHealth, crashEnriched.CodeHealth);
        Assert.Equal(crashBaseline.MethodCount, crashEnriched.MethodCount);
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

    void WriteSources(string code)
    {
        foreach (var file in Directory.GetFiles(_tempDir, "*.cs"))
            File.Delete(file);

        File.WriteAllText(Path.Combine(_tempDir, "Types.cs"), code);
    }

    static TypeMetrics FindMetrics(IReadOnlyList<TypeMetrics> metrics, string typeName)
        => metrics.Single(m => m.TypeName == typeName);

    static void AssertHealthyMetricsUnchanged(TypeMetrics expected, TypeMetrics actual)
    {
        Assert.Equal(expected.TypeName, actual.TypeName);
        Assert.Equal(expected.Namespace, actual.Namespace);
        Assert.Equal(expected.Assembly, actual.Assembly);
        Assert.Equal(expected.LineCount, actual.LineCount);
        Assert.Equal(expected.MethodCount, actual.MethodCount);
        Assert.Equal(expected.CodeHealth, actual.CodeHealth);
        Assert.Equal(expected.Lcom, actual.Lcom);
        Assert.Equal(expected.Cbo, actual.Cbo);
        Assert.Equal(expected.Dit, actual.Dit);
        Assert.Equal(expected.Rfc, actual.Rfc);
        Assert.Equal(expected.Wmc, actual.Wmc);
        Assert.Equal(expected.BoxingCount, actual.BoxingCount);
        Assert.Equal(expected.ClosureCaptureCount, actual.ClosureCaptureCount);
        Assert.Equal(expected.ParamsAllocationCount, actual.ParamsAllocationCount);
        Assert.Equal(expected.Methods.Count, actual.Methods.Count);
        for (var i = 0; i < expected.Methods.Count; i++)
            Assert.Equal(expected.Methods[i], actual.Methods[i]);
    }
}
