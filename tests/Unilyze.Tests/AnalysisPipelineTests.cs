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

        var resolved = BaseTypeResolver.ResolveTypeRelationships(
            analyzed.Types,
            analyzed.SyntaxTrees,
            new CompilationResult(compilation, AnalysisLevel.CoreEngine));

        var myBuilder = resolved.Single(t => t.Name == "MyBuilder");
        Assert.Equal("IBuilder", myBuilder.BaseType);
        Assert.Equal(["IService"], myBuilder.Interfaces);
    }

    // --- DI registration edge resolution (issue 19) ---

    // Minimal VContainer surface so builder.Register<...>() binds to a method whose
    // root namespace is "VContainer"; that namespace is what gates the semantic
    // resolution path in DIContainerAnalyzer (object.Register would not resolve).
    const string VContainerStub = """
        namespace VContainer {
            public interface IContainerBuilder { }
            public static class ContainerBuilderExtensions {
                public static void Register<TInterface, TImplementation>(this IContainerBuilder b) { }
                public static void Register<T>(this IContainerBuilder b) { }
            }
        }
        """;

    // Mirrors AnalysisPipeline.Build: analyze types + DI registrations (semantic),
    // then resolve registration endpoints to TypeIds via DITypeIdIndex.
    // extraReferences lets a test add a type that is resolvable by the compiler but
    // absent from the analyzed source set (an "external" type).
    (IReadOnlyList<TypeNodeInfo> Types, List<TypeDependency> Deps) AnalyzeWithDiEdges(
        params MetadataReference[] extraReferences)
    {
        var analyzed = TypeAnalyzer.AnalyzeDirectoryWithTrees(_tempDir, "Asm");
        var compilation = CSharpCompilation.Create(
            "Test",
            analyzed.SyntaxTrees,
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), .. extraReferences],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diRegistrations = DIContainerAnalyzer.Analyze(analyzed.SyntaxTrees, compilation);
        var index = DITypeIdIndex.Build(analyzed.Types);

        var deps = new List<TypeDependency>();
        foreach (var reg in diRegistrations)
        {
            var fromTypeId = index.Resolve(reg.ServiceType, reg.ServiceTypeQualified);
            var toTypeId = index.Resolve(reg.ImplementationType, reg.ImplementationTypeQualified);
            deps.Add(new TypeDependency(
                reg.ServiceType, reg.ImplementationType, DependencyKind.DIRegistration, fromTypeId, toTypeId));
        }

        return (analyzed.Types, deps);
    }

    // Compiles source into an in-memory assembly exposed as a MetadataReference, so a
    // test can register a type that resolves semantically yet is not part of the
    // analyzed source set.
    static MetadataReference CompileReference(string assemblyName, string code)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(code)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        stream.Position = 0;
        return MetadataReference.CreateFromStream(stream);
    }

    [Fact]
    public void DiRegistration_ResolvesEndpointsToTypeIds()
    {
        WriteFile("VContainer.cs", VContainerStub);
        WriteFile("Types.cs", """
            namespace Game.Services;

            public interface IFoo { }
            public class Foo : IFoo { }
            """);
        WriteFile("Installer.cs", """
            namespace Game;
            using Game.Services;
            using VContainer;

            public class Installer {
                public void Configure(IContainerBuilder builder) {
                    builder.Register<IFoo, Foo>();
                }
            }
            """);

        var (types, deps) = AnalyzeWithDiEdges();

        var diEdge = Assert.Single(deps, d => d.Kind == DependencyKind.DIRegistration);
        var ifoo = types.Single(t => t.Name == "IFoo");
        var foo = types.Single(t => t.Name == "Foo");
        Assert.Equal(TypeIdentity.GetTypeId(ifoo), diEdge.FromTypeId);
        Assert.Equal(TypeIdentity.GetTypeId(foo), diEdge.ToTypeId);
    }

    [Fact]
    public void DiRegistration_ResolvedEdge_ReflectsInCouplingMetrics()
    {
        WriteFile("VContainer.cs", VContainerStub);
        WriteFile("Types.cs", """
            namespace Game.Services;

            public interface IFoo { }
            public class Foo : IFoo { }
            """);
        WriteFile("Installer.cs", """
            namespace Game;
            using Game.Services;
            using VContainer;

            public class Installer {
                public void Configure(IContainerBuilder builder) {
                    builder.Register<IFoo, Foo>();
                }
            }
            """);

        var (types, deps) = AnalyzeWithDiEdges();
        var coupling = CouplingMetricsCalculator.Calculate(deps, types);

        var fooId = TypeIdentity.GetTypeId(types.Single(t => t.Name == "Foo"));
        var ifooId = TypeIdentity.GetTypeId(types.Single(t => t.Name == "IFoo"));

        // The DI edge IFoo -> Foo contributes Ce to IFoo and Ca to Foo.
        Assert.Equal(1, coupling[ifooId].EfferentCoupling);
        Assert.Equal(1, coupling[fooId].AfferentCoupling);
    }

    [Fact]
    public void DiRegistration_ExternalType_StaysUnresolved()
    {
        // The external types resolve at compile time (referenced assembly) but are not
        // part of the analyzed source set, so both endpoints stay null.
        var external = CompileReference("ExternalLib", """
            namespace ExternalOnly {
                public interface IService { }
                public class Service : IService { }
            }
            """);

        WriteFile("VContainer.cs", VContainerStub);
        WriteFile("Installer.cs", """
            namespace Game;
            using VContainer;

            public class Installer {
                public void Configure(IContainerBuilder builder) {
                    builder.Register<ExternalOnly.IService, ExternalOnly.Service>();
                }
            }
            """);

        var (_, deps) = AnalyzeWithDiEdges(external);

        var diEdge = Assert.Single(deps, d => d.Kind == DependencyKind.DIRegistration);
        Assert.Null(diEdge.FromTypeId);
        Assert.Null(diEdge.ToTypeId);
    }
}
