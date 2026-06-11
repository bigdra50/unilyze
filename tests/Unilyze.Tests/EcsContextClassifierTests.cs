using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests;

public class EcsContextClassifierTests
{
    const string EcsStub = """
        namespace Unity.Entities
        {
            public interface ISystem
            {
                void OnCreate(ref SystemState state);
                void OnUpdate(ref SystemState state);
            }
            public interface IJobEntity { }
            public interface IComponentData { }
            public struct SystemState { }
        }
        """;

    [Fact]
    public void ClassifyEcsRole_ISystem_ReturnsEcsSystem()
    {
        var (typeDecl, model) = Parse("""
            partial struct S : Unity.Entities.ISystem
            {
                public void OnCreate(ref Unity.Entities.SystemState state) { }
                public void OnUpdate(ref Unity.Entities.SystemState state) { }
            }
            """, "S");
        Assert.Equal(TypeRole.EcsSystem, EcsContextClassifier.ClassifyEcsRole(typeDecl, model));
    }

    [Fact]
    public void ClassifyEcsRole_IJobEntity_ReturnsEcsJob()
    {
        var (typeDecl, model) = Parse("partial struct J : Unity.Entities.IJobEntity { }", "J");
        Assert.Equal(TypeRole.EcsJob, EcsContextClassifier.ClassifyEcsRole(typeDecl, model));
    }

    [Fact]
    public void ClassifyEcsRole_IComponentData_ReturnsEcsComponentData()
    {
        var (typeDecl, model) = Parse("struct C : Unity.Entities.IComponentData { public int X; }", "C");
        Assert.Equal(TypeRole.EcsComponentData, EcsContextClassifier.ClassifyEcsRole(typeDecl, model));
    }

    static (TypeDeclarationSyntax TypeDecl, SemanticModel Model) Parse(string code, string typeName)
    {
        var tree = RoslynTestHelper.ParseCode(EcsStub + code);
        var compilation = CSharpCompilation.Create(
            "Test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var typeDecl = tree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == typeName);
        return (typeDecl, model);
    }
}
