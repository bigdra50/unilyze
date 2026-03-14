namespace Unilyze.Tests;

public class AsmdefInfoTests : IDisposable
{
    readonly List<string> _tempDirs = [];

    string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    void WriteAsmdef(string directory, string fileName, string json)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), json);
    }

    void WriteMeta(string directory, string fileName, string guid)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName + ".meta"), $$"""
            fileFormatVersion: 2
            guid: {{guid}}
            """);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    [Fact]
    public void Discover_EmptyDirectory_ReturnsEmptyList()
    {
        var dir = CreateTempDir();

        var result = AsmdefInfo.Discover(dir);

        Assert.Empty(result);
    }

    [Fact]
    public void Discover_SingleValidAsmdef_ParsesNameAndDirectory()
    {
        var dir = CreateTempDir();
        WriteAsmdef(dir, "MyAssembly.asmdef", """
            {"name": "MyAssembly", "references": []}
            """);

        var result = AsmdefInfo.Discover(dir);

        Assert.Single(result);
        Assert.Equal("MyAssembly", result[0].Name);
        Assert.Equal(dir, result[0].Directory);
        Assert.Empty(result[0].References);
    }

    [Fact]
    public void Discover_NestedDirectories_DiscoversAllRecursively()
    {
        var root = CreateTempDir();
        var sub1 = Path.Combine(root, "Runtime");
        var sub2 = Path.Combine(root, "Editor", "Plugins");

        WriteAsmdef(sub1, "Runtime.asmdef", """{"name": "Runtime"}""");
        WriteAsmdef(sub2, "Editor.Plugins.asmdef", """{"name": "Editor.Plugins"}""");

        var result = AsmdefInfo.Discover(root);

        Assert.Equal(2, result.Count);
        var names = result.Select(a => a.Name).OrderBy(n => n).ToList();
        Assert.Equal("Editor.Plugins", names[0]);
        Assert.Equal("Runtime", names[1]);
    }

    [Fact]
    public void Discover_NoReferencesKey_ReferencesIsEmptyList()
    {
        var dir = CreateTempDir();
        WriteAsmdef(dir, "NoRefs.asmdef", """{"name": "NoRefs"}""");

        var result = AsmdefInfo.Discover(dir);

        Assert.Single(result);
        Assert.Empty(result[0].References);
    }

    [Fact]
    public void Discover_GuidReferences_AreResolvedWhenMetaExists()
    {
        var root = CreateTempDir();
        var runtimeDir = Path.Combine(root, "Runtime");
        var featureDir = Path.Combine(root, "Feature");

        WriteAsmdef(runtimeDir, "Runtime.asmdef", """
            {
                "name": "Runtime"
            }
            """);
        WriteMeta(runtimeDir, "Runtime.asmdef", "abc123");

        WriteAsmdef(featureDir, "Feature.asmdef", """
            {
                "name": "Feature",
                "references": ["GUID:abc123", "GUID:def456"]
            }
            """);

        var result = AsmdefInfo.Discover(root);

        var feature = Assert.Single(result.Where(r => r.Name == "Feature"));
        Assert.Equal(["Runtime"], feature.References);
        Assert.Equal(["GUID:def456"], feature.UnresolvedReferences);
    }

    [Fact]
    public void Discover_MixedReferences_KeepNamedAndResolvedRefs()
    {
        var root = CreateTempDir();
        var runtimeDir = Path.Combine(root, "Runtime");
        var mixedDir = Path.Combine(root, "Mixed");

        WriteAsmdef(runtimeDir, "NamedRef.Runtime.asmdef", """
            {
                "name": "NamedRef.Runtime"
            }
            """);
        WriteMeta(runtimeDir, "NamedRef.Runtime.asmdef", "abc123");

        WriteAsmdef(mixedDir, "Mixed.asmdef", """
            {
                "name": "Mixed",
                "references": [
                    "NamedRef.Runtime",
                    "GUID:abc123",
                    "AnotherNamed",
                    "GUID:def456"
                ]
            }
            """);

        var result = AsmdefInfo.Discover(root);

        var mixed = Assert.Single(result.Where(r => r.Name == "Mixed"));
        Assert.Equal(3, mixed.References.Count);
        Assert.Equal("NamedRef.Runtime", mixed.References[0]);
        Assert.Equal("NamedRef.Runtime", mixed.References[1]);
        Assert.Equal("AnotherNamed", mixed.References[2]);
        Assert.Equal(["GUID:def456"], mixed.UnresolvedReferences);
    }

    [Fact]
    public void Discover_NullName_FallsBackToFilename()
    {
        var dir = CreateTempDir();
        // "name" is null -> falls back to Path.GetFileNameWithoutExtension
        WriteAsmdef(dir, "FallbackName.asmdef", """{"name": null, "references": []}""");

        var result = AsmdefInfo.Discover(dir);

        Assert.Single(result);
        Assert.Equal("FallbackName", result[0].Name);
    }

    [Fact]
    public void Discover_MultipleAsmdefs_VariousReferencePatterns()
    {
        var root = CreateTempDir();
        var dirA = Path.Combine(root, "A");
        var dirB = Path.Combine(root, "B");
        var dirC = Path.Combine(root, "Sub", "C");

        WriteAsmdef(dirA, "A.asmdef", """
            {"name": "A", "references": ["B", "C"]}
            """);
        WriteMeta(dirA, "A.asmdef", "guid-a");
        WriteAsmdef(dirB, "B.asmdef", """
            {"name": "B", "references": ["GUID:guid-a", "A"]}
            """);
        WriteMeta(dirB, "B.asmdef", "guid-b");
        WriteAsmdef(dirC, "C.asmdef", """
            {"name": "C"}
            """);
        WriteMeta(dirC, "C.asmdef", "guid-c");

        var result = AsmdefInfo.Discover(root);

        Assert.Equal(3, result.Count);

        var byName = result.ToDictionary(a => a.Name);

        Assert.Equal(2, byName["A"].References.Count);
        Assert.Contains("B", byName["A"].References);
        Assert.Contains("C", byName["A"].References);

        Assert.Equal(["A", "A"], byName["B"].References);
        Assert.Null(byName["B"].UnresolvedReferences);

        Assert.Empty(byName["C"].References);
    }
}
