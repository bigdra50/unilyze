using Unilyze;

namespace Unilyze.Tests.Config;

public sealed class UnilyzeConfigTests : IDisposable
{
    readonly string _tempDir;

    public UnilyzeConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-config-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, true);

    string WriteTempFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    // --- LoadFile ---

    [Fact]
    public void LoadFile_MaxParallelism_ParsesValue()
    {
        var path = WriteTempFile("config.json", """{ "maxParallelism": 4 }""");
        var result = UnilyzeConfig.LoadFile(path);
        Assert.Equal(4, result.MaxParallelism);
    }

    [Fact]
    public void ResolveMaxParallelism_UsesProcessorCountWhenUnset()
    {
        Assert.Equal(Environment.ProcessorCount, UnilyzeConfig.ResolveMaxParallelism(null));
    }

    [Fact]
    public void ResolveMaxParallelism_UsesConfigWhenPositive()
    {
        Assert.Equal(2, UnilyzeConfig.ResolveMaxParallelism(2));
    }

    [Fact]
    public void Merge_MaxParallelism_HigherWins()
    {
        var lower = new UnilyzeConfig(MaxParallelism: 2);
        var higher = new UnilyzeConfig(MaxParallelism: 8);
        var result = UnilyzeConfig.Merge(lower, higher);
        Assert.Equal(8, result.MaxParallelism);
    }

    [Fact]
    public void Merge_EditorCommand_HigherWins()
    {
        var lower = new UnilyzeConfig(EditorCommand: "vscode");
        var higher = new UnilyzeConfig(EditorCommand: "cursor");

        var result = UnilyzeConfig.Merge(lower, higher);

        Assert.Equal("cursor", result.EditorCommand);
    }

    [Fact]
    public void LoadFile_EditorCommand_ParsesValue()
    {
        var path = WriteTempFile("config.json", """{ "editorCommand": "idea" }""");

        var result = UnilyzeConfig.LoadFile(path);

        Assert.Equal("idea", result.EditorCommand);
    }

    [Fact]
    public void LoadFile_NonExistent_ReturnsEmpty()
    {
        var result = UnilyzeConfig.LoadFile(Path.Combine(_tempDir, "nonexistent.json"));
        Assert.Same(UnilyzeConfig.Empty, result);
    }

    [Fact]
    public void LoadFile_ValidJson_ParsesExcludeDirs()
    {
        var path = WriteTempFile("config.json", """
            {
                "excludeDirs": ["Assets/Plugins", "Assets/ThirdParty"]
            }
            """);
        var result = UnilyzeConfig.LoadFile(path);
        Assert.NotNull(result.ExcludeDirs);
        Assert.Equal(2, result.ExcludeDirs!.Count);
        Assert.Contains("Assets/Plugins", result.ExcludeDirs);
        Assert.Contains("Assets/ThirdParty", result.ExcludeDirs);
    }

    [Fact]
    public void LoadFile_JsonWithComments_ParsesCorrectly()
    {
        var path = WriteTempFile("config.json", """
            {
                // This is a comment
                "excludeDirs": ["Assets/Plugins"],
            }
            """);
        var result = UnilyzeConfig.LoadFile(path);
        Assert.NotNull(result.ExcludeDirs);
        Assert.Single(result.ExcludeDirs!);
        Assert.Equal("Assets/Plugins", result.ExcludeDirs![0]);
    }

    [Fact]
    public void LoadFile_InvalidJson_ReturnsEmpty()
    {
        var path = WriteTempFile("config.json", "{ invalid json }");
        var result = UnilyzeConfig.LoadFile(path);
        Assert.Same(UnilyzeConfig.Empty, result);
    }

    [Fact]
    public void LoadFile_EmptyObject_ReturnsConfigWithNullExcludeDirs()
    {
        var path = WriteTempFile("config.json", "{}");
        var result = UnilyzeConfig.LoadFile(path);
        Assert.Null(result.ExcludeDirs);
    }

    // --- Merge ---

    [Fact]
    public void Merge_BothEmpty_ReturnsEmpty()
    {
        var result = UnilyzeConfig.Merge(UnilyzeConfig.Empty, UnilyzeConfig.Empty);
        Assert.Null(result.ExcludeDirs);
    }

    [Fact]
    public void Merge_LowerNullHigherHasValues_ReturnsHigher()
    {
        var higher = new UnilyzeConfig(["Assets/Plugins"]);
        var result = UnilyzeConfig.Merge(UnilyzeConfig.Empty, higher);
        Assert.Equal(higher.ExcludeDirs, result.ExcludeDirs);
        Assert.Equal(higher.DisableDefaultExcludes, result.DisableDefaultExcludes);
        Assert.Equal(higher.DisableGeneratedCodeExcludes, result.DisableGeneratedCodeExcludes);
    }

    [Fact]
    public void Merge_LowerHasValuesHigherNull_ReturnsLower()
    {
        var lower = new UnilyzeConfig(["Assets/Plugins"]);
        var result = UnilyzeConfig.Merge(lower, UnilyzeConfig.Empty);
        Assert.Equal(lower.ExcludeDirs, result.ExcludeDirs);
        Assert.Equal(lower.DisableDefaultExcludes, result.DisableDefaultExcludes);
        Assert.Equal(lower.DisableGeneratedCodeExcludes, result.DisableGeneratedCodeExcludes);
    }

    [Fact]
    public void Merge_BothHaveValues_UnionsAndDeduplicates()
    {
        var lower = new UnilyzeConfig(["Assets/Plugins", "Assets/Common"]);
        var higher = new UnilyzeConfig(["Assets/ThirdParty", "Assets/Plugins"]);
        var result = UnilyzeConfig.Merge(lower, higher);

        Assert.NotNull(result.ExcludeDirs);
        Assert.Equal(3, result.ExcludeDirs!.Count);
        Assert.Contains("Assets/Plugins", result.ExcludeDirs);
        Assert.Contains("Assets/Common", result.ExcludeDirs);
        Assert.Contains("Assets/ThirdParty", result.ExcludeDirs);
    }

    [Fact]
    public void Merge_DeduplicationIsCaseInsensitive()
    {
        var lower = new UnilyzeConfig(["Assets/Plugins"]);
        var higher = new UnilyzeConfig(["assets/plugins"]);
        var result = UnilyzeConfig.Merge(lower, higher);

        Assert.NotNull(result.ExcludeDirs);
        Assert.Single(result.ExcludeDirs!);
    }

    // --- ResolveExcludePaths ---

    [Fact]
    public void ResolveExcludePaths_RelativeToProjectRoot()
    {
        var projectRoot = _tempDir;
        var paths = new List<string> { "Assets/Plugins", "Assets/ThirdParty" };
        var resolved = UnilyzeConfig.ResolveExcludePaths(paths, projectRoot);

        Assert.Equal(2, resolved.Count);
        Assert.Equal(Path.GetFullPath(Path.Combine(projectRoot, "Assets/Plugins")), resolved[0]);
        Assert.Equal(Path.GetFullPath(Path.Combine(projectRoot, "Assets/ThirdParty")), resolved[1]);
    }

    [Fact]
    public void ResolveExcludePaths_AbsolutePathsPreserved()
    {
        var absPath = Path.Combine(_tempDir, "SomeAbsDir");
        var paths = new List<string> { absPath };
        var resolved = UnilyzeConfig.ResolveExcludePaths(paths, "/other/root");

        Assert.Single(resolved);
        Assert.Equal(Path.GetFullPath(absPath), resolved[0]);
    }

    // --- LoadMerged ---

    [Fact]
    public void LoadMerged_ProjectConfig_AppliesExcludes()
    {
        var projectRoot = _tempDir;
        WriteTempFile(".unilyze.json", """
            { "excludeDirs": ["Assets/Plugins"] }
            """);

        var result = UnilyzeConfig.LoadMerged(projectRoot);

        Assert.NotNull(result.ExcludeDirs);
        Assert.Contains(
            Path.GetFullPath(Path.Combine(projectRoot, "Assets/Plugins")),
            result.ExcludeDirs!);
        Assert.Contains(result.ExcludeDirs!,
            dir => dir.EndsWith($"{Path.DirectorySeparatorChar}obj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadMerged_CliExcludesAdded()
    {
        var projectRoot = _tempDir;
        WriteTempFile(".unilyze.json", """
            { "excludeDirs": ["Assets/Plugins"] }
            """);

        var result = UnilyzeConfig.LoadMerged(projectRoot, ["Assets/Tests"]);

        Assert.NotNull(result.ExcludeDirs);
        Assert.Contains(Path.GetFullPath(Path.Combine(projectRoot, "Assets/Plugins")), result.ExcludeDirs!);
        Assert.Contains(Path.GetFullPath(Path.Combine(projectRoot, "Assets/Tests")), result.ExcludeDirs!);
        Assert.Contains(result.ExcludeDirs!,
            dir => dir.EndsWith($"{Path.DirectorySeparatorChar}obj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadMerged_NoConfigFiles_CliOnly()
    {
        var projectRoot = _tempDir;
        var result = UnilyzeConfig.LoadMerged(projectRoot, ["Assets/Plugins"]);

        Assert.NotNull(result.ExcludeDirs);
        Assert.Contains(Path.GetFullPath(Path.Combine(projectRoot, "Assets/Plugins")), result.ExcludeDirs!);
        Assert.Contains(result.ExcludeDirs!,
            dir => dir.EndsWith($"{Path.DirectorySeparatorChar}obj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadMerged_NoConfigNoCli_AppliesDefaultExcludeDirs()
    {
        var projectRoot = _tempDir;
        var result = UnilyzeConfig.LoadMerged(projectRoot);

        Assert.NotNull(result.ExcludeDirs);
        Assert.Contains(result.ExcludeDirs!,
            dir => dir.EndsWith($"{Path.DirectorySeparatorChar}obj", StringComparison.OrdinalIgnoreCase));
        Assert.False(result.DisableDefaultExcludes);
        Assert.False(result.DisableGeneratedCodeExcludes);
    }

    [Fact]
    public void LoadMerged_DisableDefaultExcludes_SkipsBuiltInDirs()
    {
        WriteTempFile(".unilyze.json", """
            { "disableDefaultExcludes": true }
            """);

        var result = UnilyzeConfig.LoadMerged(_tempDir);

        Assert.Null(result.ExcludeDirs);
        Assert.True(result.DisableDefaultExcludes);
    }

    [Fact]
    public void Merge_BooleanFlags_UnionDisableFlags()
    {
        var lower = new UnilyzeConfig(DisableDefaultExcludes: true);
        var higher = new UnilyzeConfig(DisableGeneratedCodeExcludes: true);
        var result = UnilyzeConfig.Merge(lower, higher);

        Assert.True(result.DisableDefaultExcludes);
        Assert.True(result.DisableGeneratedCodeExcludes);
    }

    // --- SaveFile ---

    [Fact]
    public void SaveFile_CreatesDirectoryAndWritesJson()
    {
        var path = Path.Combine(_tempDir, "sub", "config.json");
        var config = new UnilyzeConfig(["Assets/Plugins"]);

        UnilyzeConfig.SaveFile(path, config);

        Assert.True(File.Exists(path));
        var loaded = UnilyzeConfig.LoadFile(path);
        Assert.NotNull(loaded.ExcludeDirs);
        Assert.Single(loaded.ExcludeDirs!);
        Assert.Equal("Assets/Plugins", loaded.ExcludeDirs![0]);
    }

    [Fact]
    public void SaveFile_NullExcludeDirs_OmitsKey()
    {
        var path = Path.Combine(_tempDir, "empty.json");
        UnilyzeConfig.SaveFile(path, UnilyzeConfig.Empty);

        var json = File.ReadAllText(path);
        Assert.DoesNotContain("excludeDirs", json);
    }

    // --- AddExcludeDir ---

    [Fact]
    public void AddExcludeDir_NewFile_CreatesWithEntry()
    {
        var path = Path.Combine(_tempDir, ".unilyze.json");
        var result = UnilyzeConfig.AddExcludeDir(path, "Assets/Plugins");

        Assert.True(result);
        var config = UnilyzeConfig.LoadFile(path);
        Assert.Single(config.ExcludeDirs!);
        Assert.Equal("Assets/Plugins", config.ExcludeDirs![0]);
    }

    [Fact]
    public void AddExcludeDir_ExistingFile_AppendsEntry()
    {
        var path = WriteTempFile(".unilyze.json", """{ "excludeDirs": ["Assets/Plugins"] }""");
        var result = UnilyzeConfig.AddExcludeDir(path, "Assets/ThirdParty");

        Assert.True(result);
        var config = UnilyzeConfig.LoadFile(path);
        Assert.Equal(2, config.ExcludeDirs!.Count);
    }

    [Fact]
    public void AddExcludeDir_Duplicate_ReturnsFalse()
    {
        var path = WriteTempFile(".unilyze.json", """{ "excludeDirs": ["Assets/Plugins"] }""");
        var result = UnilyzeConfig.AddExcludeDir(path, "Assets/Plugins");

        Assert.False(result);
    }

    [Fact]
    public void AddExcludeDir_DuplicateCaseInsensitive_ReturnsFalse()
    {
        var path = WriteTempFile(".unilyze.json", """{ "excludeDirs": ["Assets/Plugins"] }""");
        var result = UnilyzeConfig.AddExcludeDir(path, "assets/plugins");

        Assert.False(result);
    }

    // --- RemoveExcludeDir ---

    [Fact]
    public void RemoveExcludeDir_ExistingEntry_RemovesIt()
    {
        var path = WriteTempFile(".unilyze.json",
            """{ "excludeDirs": ["Assets/Plugins", "Assets/ThirdParty"] }""");
        var result = UnilyzeConfig.RemoveExcludeDir(path, "Assets/Plugins");

        Assert.True(result);
        var config = UnilyzeConfig.LoadFile(path);
        Assert.Single(config.ExcludeDirs!);
        Assert.Equal("Assets/ThirdParty", config.ExcludeDirs![0]);
    }

    [Fact]
    public void RemoveExcludeDir_LastEntry_RemovesKey()
    {
        var path = WriteTempFile(".unilyze.json", """{ "excludeDirs": ["Assets/Plugins"] }""");
        var result = UnilyzeConfig.RemoveExcludeDir(path, "Assets/Plugins");

        Assert.True(result);
        var config = UnilyzeConfig.LoadFile(path);
        Assert.Null(config.ExcludeDirs);
    }

    [Fact]
    public void RemoveExcludeDir_NotFound_ReturnsFalse()
    {
        var path = WriteTempFile(".unilyze.json", """{ "excludeDirs": ["Assets/Plugins"] }""");
        var result = UnilyzeConfig.RemoveExcludeDir(path, "Assets/NonExistent");

        Assert.False(result);
    }

    [Fact]
    public void RemoveExcludeDir_EmptyConfig_ReturnsFalse()
    {
        var path = WriteTempFile(".unilyze.json", "{}");
        var result = UnilyzeConfig.RemoveExcludeDir(path, "Assets/Plugins");

        Assert.False(result);
    }

    // --- Path helpers ---

    [Fact]
    public void GetProjectConfigPath_ReturnsExpected()
    {
        var path = UnilyzeConfig.GetProjectConfigPath("/my/project");
        Assert.Equal(Path.Combine("/my/project", ".unilyze.json"), path);
    }
}
