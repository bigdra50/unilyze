using Unilyze;

namespace Unilyze.Tests;

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
        Assert.Same(higher, result);
    }

    [Fact]
    public void Merge_LowerHasValuesHigherNull_ReturnsLower()
    {
        var lower = new UnilyzeConfig(["Assets/Plugins"]);
        var result = UnilyzeConfig.Merge(lower, UnilyzeConfig.Empty);
        Assert.Same(lower, result);
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
        Assert.Single(result.ExcludeDirs!);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(projectRoot, "Assets/Plugins")),
            result.ExcludeDirs![0]);
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
        Assert.Equal(2, result.ExcludeDirs!.Count);
    }

    [Fact]
    public void LoadMerged_NoConfigFiles_CliOnly()
    {
        var projectRoot = _tempDir;
        var result = UnilyzeConfig.LoadMerged(projectRoot, ["Assets/Plugins"]);

        Assert.NotNull(result.ExcludeDirs);
        Assert.Single(result.ExcludeDirs!);
    }

    [Fact]
    public void LoadMerged_NoConfigNoCliReturnsNullExcludeDirs()
    {
        var projectRoot = _tempDir;
        var result = UnilyzeConfig.LoadMerged(projectRoot);

        Assert.Null(result.ExcludeDirs);
    }

    // --- Path helpers ---

    [Fact]
    public void GetProjectConfigPath_ReturnsExpected()
    {
        var path = UnilyzeConfig.GetProjectConfigPath("/my/project");
        Assert.Equal(Path.Combine("/my/project", ".unilyze.json"), path);
    }
}
