namespace Unilyze.Tests;

public sealed class DotnetSemanticPipelineTests : IDisposable
{
    readonly string _tempDir;

    public DotnetSemanticPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Unilyze_DotnetSemantic_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void Build_NonUnityFixture_ProducesSemanticMetrics()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Sample.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk" />
            """);
        File.WriteAllText(Path.Combine(_tempDir, "Types.cs"), """
            namespace Sample;

            public class Base { }
            public class Derived : Base { }

            public static class BoxingSample
            {
                public static int Count(object value) => 1;
                public static void Run()
                {
                    Count(42);
                }
            }
            """);

        var result = AnalysisPipeline.Build(_tempDir, null, null);

        Assert.Equal("dotnet", result.ProjectKind);
        Assert.Equal("Complete", result.AnalysisLevel);
        Assert.NotNull(result.TypeMetrics);

        var withCbo = result.TypeMetrics!.Count(m => m.Cbo is not null);
        var withDit = result.TypeMetrics.Count(m => m.Dit is not null);
        var withBoxing = result.TypeMetrics.Count(m => m.BoxingCount is > 0);

        Assert.True(withCbo > 0, "Expected non-null CBO values");
        Assert.True(withDit > 0, "Expected non-null DIT values");
        Assert.True(withBoxing > 0, "Expected non-zero boxing counts");
    }

    [Fact]
    public void Build_SyntaxPin_SkipsSemanticModel()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Sample.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk" />
            """);
        File.WriteAllText(Path.Combine(_tempDir, "Types.cs"), """
            namespace Sample;
            public class Derived : Base { }
            public class Base { }
            """);

        var result = AnalysisPipeline.Build(_tempDir, null, null, requestedLevel: AnalysisLevel.Syntax);

        Assert.Equal("SyntaxOnly", result.AnalysisLevel);
        Assert.All(result.TypeMetrics ?? [], m => Assert.Null(m.BoxingCount));
    }
}
