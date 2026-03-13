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
    public void Discover_GuidReferences_AreSkipped()
    {
        var dir = CreateTempDir();
        WriteAsmdef(dir, "GuidOnly.asmdef", """
            {
                "name": "GuidOnly",
                "references": ["GUID:abc123", "GUID:def456"]
            }
            """);

        var result = AsmdefInfo.Discover(dir);

        Assert.Single(result);
        Assert.Empty(result[0].References);
    }

    [Fact]
    public void Discover_MixedReferences_OnlyNamedRefsKept()
    {
        var dir = CreateTempDir();
        WriteAsmdef(dir, "Mixed.asmdef", """
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

        var result = AsmdefInfo.Discover(dir);

        Assert.Single(result);
        Assert.Equal(2, result[0].References.Count);
        Assert.Equal("NamedRef.Runtime", result[0].References[0]);
        Assert.Equal("AnotherNamed", result[0].References[1]);
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
        WriteAsmdef(dirB, "B.asmdef", """
            {"name": "B", "references": ["GUID:guid1", "A"]}
            """);
        WriteAsmdef(dirC, "C.asmdef", """
            {"name": "C"}
            """);

        var result = AsmdefInfo.Discover(root);

        Assert.Equal(3, result.Count);

        var byName = result.ToDictionary(a => a.Name);

        Assert.Equal(2, byName["A"].References.Count);
        Assert.Contains("B", byName["A"].References);
        Assert.Contains("C", byName["A"].References);

        Assert.Single(byName["B"].References);
        Assert.Equal("A", byName["B"].References[0]);

        Assert.Empty(byName["C"].References);
    }
}
