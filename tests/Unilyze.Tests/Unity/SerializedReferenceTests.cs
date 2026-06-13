namespace Unilyze.Tests.Unity;

public sealed class SerializedReferenceTests : IDisposable
{
    readonly string _tempDir;

    public SerializedReferenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Unilyze_SerializedReference_" + Path.GetRandomFileName());
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

    void WriteUnityProject(string sceneYaml, string? prefabYaml = null)
    {
        WriteFile("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 2022.3.0f1\n");
        WriteFile("Assets/Scripts/BaseSpawner.cs", """
            namespace Sample;
            public abstract class BaseSpawner : UnityEngine.MonoBehaviour { }
            """);
        WriteFile("Assets/Scripts/EnemySpawner.cs", """
            namespace Sample;
            public class EnemySpawner : BaseSpawner { }
            """);
        WriteFile("Assets/Scripts/SpawnerHost.cs", """
            namespace Sample;
            public class SpawnerHost : UnityEngine.MonoBehaviour
            {
                [UnityEngine.SerializeField] BaseSpawner spawner;
            }
            """);
        WriteFile("Assets/Scripts/UnityEngineStubs.cs", """
            namespace UnityEngine;
            [System.AttributeUsage(System.AttributeTargets.Field)]
            public class SerializeFieldAttribute : System.Attribute { }
            public class MonoBehaviour { }
            """);
        WriteFile("Assets/Scripts/BaseSpawner.cs.meta", "fileFormatVersion: 2\nguid: aaa0000000000000000000000000001\n");
        WriteFile("Assets/Scripts/EnemySpawner.cs.meta", "fileFormatVersion: 2\nguid: aaa0000000000000000000000000002\n");
        WriteFile("Assets/Scripts/SpawnerHost.cs.meta", "fileFormatVersion: 2\nguid: aaa0000000000000000000000000003\n");
        WriteFile("Assets/Scenes/Test.unity", sceneYaml);
        if (prefabYaml is not null)
        {
            WriteFile("Assets/Prefabs/Target.prefab.meta", "fileFormatVersion: 2\nguid: bbb0000000000000000000000000001\n");
            WriteFile("Assets/Prefabs/Target.prefab", prefabYaml);
        }
    }

    AnalysisResult AnalyzeProject()
        => AnalysisPipeline.Build(_tempDir, null, null, excludeGeneratedCode: false);

    [Fact]
    public void Parser_ReadsMonoBehaviourScriptGuidAndInlineReference()
    {
        var yaml = """
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!114 &42
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: abcdef0123456789abcdef0123456789, type: 3}
              spawner: {fileID: 99}
            --- !u!114 &99
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: fedcba9876543210fedcba9876543210, type: 3}
            """;

        WriteFile("test.unity", yaml);
        var parsed = UnitySceneReferenceParser.TryParseFile(Path.Combine(_tempDir, "test.unity"));

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed!.MonoBehaviours.Count);
        var host = parsed.MonoBehaviours[42];
        Assert.Equal("abcdef0123456789abcdef0123456789", host.ScriptGuid);
        Assert.True(host.FieldReferences.TryGetValue("spawner", out var refs));
        Assert.Single(refs);
        Assert.Equal(99, refs[0].FileId);
        Assert.Null(refs[0].Guid);
    }

    [Fact]
    public void Parser_ReadsSequenceReferences()
    {
        var yaml = """
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!114 &1
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: abcdef0123456789abcdef0123456789, type: 3}
              targets:
              - {fileID: 2}
              - {fileID: 3}
            """;

        WriteFile("test.unity", yaml);
        var parsed = UnitySceneReferenceParser.TryParseFile(Path.Combine(_tempDir, "test.unity"));

        Assert.NotNull(parsed);
        var refs = parsed!.MonoBehaviours[1].FieldReferences["targets"];
        Assert.Equal(2, refs.Count);
        Assert.Equal(2, refs[0].FileId);
        Assert.Equal(3, refs[1].FileId);
    }

    [Fact]
    public void Parser_ReadsCrossFileGuidReference()
    {
        var yaml = """
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!114 &1
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: abcdef0123456789abcdef0123456789, type: 3}
              spawner: {fileID: 100, guid: bbbb0000000000000000000000000001, type: 3}
            """;

        WriteFile("test.unity", yaml);
        var parsed = UnitySceneReferenceParser.TryParseFile(Path.Combine(_tempDir, "test.unity"));

        Assert.NotNull(parsed);
        var refs = parsed!.MonoBehaviours[1].FieldReferences["spawner"];
        Assert.Equal("bbbb0000000000000000000000000001", refs[0].Guid);
        Assert.Equal(100, refs[0].FileId);
    }

    [Fact]
    public void Parser_SkipsBinarySerializedAssets()
    {
        WriteFile("binary.asset", "\0\0\0\0not yaml");
        Assert.Null(UnitySceneReferenceParser.TryParseFile(Path.Combine(_tempDir, "binary.asset")));
    }

    [Fact]
    public void Parser_IgnoresUnityBuiltInMFields()
    {
        var yaml = """
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!114 &1
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: abcdef0123456789abcdef0123456789, type: 3}
              m_GameObject: {fileID: 2}
            """;

        WriteFile("test.unity", yaml);
        var parsed = UnitySceneReferenceParser.TryParseFile(Path.Combine(_tempDir, "test.unity"));

        Assert.NotNull(parsed);
        Assert.DoesNotContain("m_GameObject", parsed!.MonoBehaviours[1].FieldReferences.Keys);
    }

    [Fact]
    public void Resolver_EmitsEdgeForBaseTypedFieldWiredToConcreteType()
    {
        WriteUnityProject("""
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!114 &10
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: aaa0000000000000000000000000003, type: 3}
              spawner: {fileID: 20}
            --- !u!114 &20
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: aaa0000000000000000000000000002, type: 3}
            """);

        var result = AnalyzeProject();
        var edge = Assert.Single(result.Dependencies, d => d.Kind == DependencyKind.SerializedReference);

        Assert.Equal("SpawnerHost", edge.FromType);
        Assert.Equal("EnemySpawner", edge.ToType);
        Assert.NotNull(edge.FromTypeId);
        Assert.NotNull(edge.ToTypeId);
    }

    [Fact]
    public void Resolver_SkipsUnassignedFileIdZero()
    {
        WriteUnityProject("""
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!114 &10
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: aaa0000000000000000000000000003, type: 3}
              spawner: {fileID: 0}
            """);

        var result = AnalyzeProject();
        Assert.DoesNotContain(result.Dependencies, d => d.Kind == DependencyKind.SerializedReference);
    }

    [Fact]
    public void Resolver_ResolvesCrossFilePrefabReference()
    {
        WriteUnityProject(
            """
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!114 &10
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: aaa0000000000000000000000000003, type: 3}
              spawner: {fileID: 100, guid: bbb0000000000000000000000000001, type: 3}
            """,
            """
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!114 &100
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: aaa0000000000000000000000000002, type: 3}
            """);

        var result = AnalyzeProject();
        var edge = Assert.Single(result.Dependencies, d => d.Kind == DependencyKind.SerializedReference);
        Assert.Equal("EnemySpawner", edge.ToType);
    }

    [Fact]
    public void Resolver_SkipsWhenScriptMetaMissing()
    {
        WriteUnityProject("""
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!114 &10
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: missing000000000000000000000001, type: 3}
              spawner: {fileID: 20}
            --- !u!114 &20
            MonoBehaviour:
              m_Script: {fileID: 11500000, guid: aaa0000000000000000000000000002, type: 3}
            """);

        var result = AnalyzeProject();
        Assert.DoesNotContain(result.Dependencies, d => d.Kind == DependencyKind.SerializedReference);
    }

    [Fact]
    public void NonUnityProject_SkipsSerializedReferenceScan()
    {
        WriteFile("MyApp.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        WriteFile("Program.cs", "class Program { static void Main() {} }");
        WriteFile("Assets/Scenes/Test.unity", "%YAML 1.1\n--- !u!114 &1\nMonoBehaviour:\n  m_Script: {fileID: 11500000, guid: abc, type: 3}\n");

        var result = AnalysisPipeline.Build(_tempDir, null, null);
        Assert.DoesNotContain(result.Dependencies, d => d.Kind == DependencyKind.SerializedReference);
    }

    [Fact]
    public void GoldenFixture_SerializedReferenceChangesCouplingNotCbo()
    {
        var fixtureRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "golden"));
        var result = AnalyzeGoldenFixture(fixtureRoot);

        var hostId = "Assembly-CSharp::GoldenFixture.MonoBehaviourGodClassTarget";
        var concreteId = "Assembly-CSharp::GoldenFixture.SerializedRefConcrete";

        var afterHost = result.TypeMetrics!.Single(m => TypeIdentity.GetTypeId(m) == hostId);
        Assert.True(afterHost.Cbo >= 1);
        Assert.True(afterHost.EfferentCoupling >= 1);

        var concreteMetric = result.TypeMetrics!.Single(m => TypeIdentity.GetTypeId(m) == concreteId);
        Assert.True(concreteMetric.AfferentCoupling >= 1);

        var edge = Assert.Single(result.Dependencies, d =>
            d.Kind == DependencyKind.SerializedReference && d.FromTypeId == hostId);
        Assert.Equal(concreteId, edge.ToTypeId);
    }

    static AnalysisResult AnalyzeGoldenFixture(string fixtureRoot)
    {
        var csprojPath = Path.Combine(fixtureRoot, "Golden.csproj");
        File.WriteAllText(csprojPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Reference Include="CoreLib">
                  <HintPath>{typeof(object).Assembly.Location}</HintPath>
                </Reference>
              </ItemGroup>
            </Project>
            """);

        return AnalysisPipeline.Build(fixtureRoot, null, null, excludeGeneratedCode: false);
    }
}
