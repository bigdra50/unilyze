using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests;

public class EcsSmellDetectorTests
{
    const string EcsStub = """
        namespace Unity.Entities
        {
            public interface ISystem
            {
                void OnCreate(ref SystemState state);
                void OnUpdate(ref SystemState state);
                void OnDestroy(ref SystemState state);
            }
            public class SystemBase { }
            public interface IJobEntity { }
            public interface IJobChunk { }
            public interface IComponentData { }
            public struct SystemState { }
            public struct Entity { public int Index; }
        }
        namespace Unity.Burst
        {
            public class BurstCompileAttribute : System.Attribute { }
        }
        """;

    static readonly ISmellDetector BurstDetector = new EcsBurstSmellDetector();
    static readonly ISmellDetector ManagedDetector = new ManagedComponentDataSmellDetector();

    static (TypeDeclarationSyntax TypeDecl, SemanticModel? Model) Parse(
        string code,
        string typeName,
        bool semantic = true)
    {
        var fullCode = semantic ? EcsStub + code : code;
        if (semantic)
        {
            var tree = RoslynTestHelper.ParseCode(fullCode);
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

        return (RoslynTestHelper.GetType(fullCode, typeName), null);
    }

    static List<DetectedSmell> DetectBurst(string code, string typeName, bool semantic = true)
    {
        var (typeDecl, model) = Parse(code, typeName, semantic);
        return BurstDetector.Detect(typeDecl, model).ToList();
    }

    static List<DetectedSmell> DetectManaged(string code, string typeName, bool semantic = true)
    {
        var (typeDecl, model) = Parse(code, typeName, semantic);
        return ManagedDetector.Detect(typeDecl, model).ToList();
    }

    [Fact]
    public void Detect_UnannotatedISystem_ReportsMissingBurstCompile()
    {
        var smells = DetectBurst("""
            partial struct PlayerSystem : Unity.Entities.ISystem
            {
                public void OnCreate(ref Unity.Entities.SystemState state) { }
                public void OnUpdate(ref Unity.Entities.SystemState state) { }
                public void OnDestroy(ref Unity.Entities.SystemState state) { }
            }
            """, "PlayerSystem");

        Assert.Single(smells);
        Assert.Equal(CodeSmellKind.MissingBurstCompile, smells[0].Kind);
        Assert.Equal(SmellSeverity.Warning, smells[0].Severity);
    }

    [Fact]
    public void Detect_AnnotatedISystem_NoSmell()
    {
        var smells = DetectBurst("""
            [Unity.Burst.BurstCompile]
            partial struct PlayerSystem : Unity.Entities.ISystem
            {
                public void OnCreate(ref Unity.Entities.SystemState state) { }
                public void OnUpdate(ref Unity.Entities.SystemState state) { }
                public void OnDestroy(ref Unity.Entities.SystemState state) { }
            }
            """, "PlayerSystem");

        Assert.Empty(smells);
    }

    [Fact]
    public void Detect_ISystemWithAnnotatedLifecycle_NoSmell()
    {
        var smells = DetectBurst("""
            partial struct PlayerSystem : Unity.Entities.ISystem
            {
                [Unity.Burst.BurstCompile] public void OnCreate(ref Unity.Entities.SystemState state) { }
                [Unity.Burst.BurstCompile] public void OnUpdate(ref Unity.Entities.SystemState state) { }
                [Unity.Burst.BurstCompile] public void OnDestroy(ref Unity.Entities.SystemState state) { }
            }
            """, "PlayerSystem");

        Assert.Empty(smells);
    }

    [Fact]
    public void Detect_UnannotatedIJobEntity_ReportsMissingBurstCompile()
    {
        var smells = DetectBurst("""
            partial struct MoveJob : Unity.Entities.IJobEntity { }
            """, "MoveJob");

        Assert.Single(smells);
        Assert.Equal(CodeSmellKind.MissingBurstCompile, smells[0].Kind);
    }

    [Fact]
    public void Detect_AnnotatedIJobChunk_NoSmell()
    {
        var smells = DetectBurst("""
            [Unity.Burst.BurstCompile]
            partial struct ChunkJob : Unity.Entities.IJobChunk { }
            """, "ChunkJob");

        Assert.Empty(smells);
    }

    [Fact]
    public void Detect_SystemBase_NoBurstSmell()
    {
        var smells = DetectBurst("""
            class ManagedSystem : Unity.Entities.SystemBase { }
            """, "ManagedSystem");

        Assert.Empty(smells);
    }

    [Fact]
    public void Detect_StructIComponentDataWithString_ReportsManagedReference()
    {
        var smells = DetectManaged("""
            struct Health : Unity.Entities.IComponentData
            {
                public string Label;
            }
            """, "Health");

        Assert.Single(smells);
        Assert.Equal(CodeSmellKind.ManagedReferenceInComponentData, smells[0].Kind);
        Assert.Equal("Label", smells[0].MethodName);
    }

    [Fact]
    public void Detect_UnmanagedIComponentData_NoSmell()
    {
        var smells = DetectManaged("""
            struct Health : Unity.Entities.IComponentData
            {
                public Unity.Entities.Entity Target;
                public float Value;
            }
            """, "Health");

        Assert.Empty(smells);
    }

    [Fact]
    public void Detect_ClassIComponentData_NoSmell()
    {
        var smells = DetectManaged("""
            class Health : Unity.Entities.IComponentData
            {
                public string Label;
            }
            """, "Health");

        Assert.Empty(smells);
    }

    [Fact]
    public void Detect_SyntaxOnly_FlagsByInterfaceName()
    {
        var smells = DetectBurst("""
            partial struct PlayerSystem : ISystem
            {
                public void OnCreate(ref SystemState state) { }
                public void OnUpdate(ref SystemState state) { }
            }
            """, "PlayerSystem", semantic: false);

        Assert.Single(smells);
    }

    [Fact]
    public void Detect_SemanticMode_IgnoresNonUnityISystem()
    {
        var code = """
            namespace Acme.Framework
            {
                public interface ISystem { }
            }
            namespace Game
            {
                partial struct FakeSystem : Acme.Framework.ISystem { }
            }
            """ + EcsStub;

        var tree = RoslynTestHelper.ParseCode(code);
        var compilation = CSharpCompilation.Create(
            "Test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var typeDecl = tree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == "FakeSystem");

        var smells = BurstDetector.Detect(typeDecl, model);
        Assert.Empty(smells);
    }
}
