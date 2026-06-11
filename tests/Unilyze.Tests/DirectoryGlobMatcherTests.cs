namespace Unilyze.Tests;

public sealed class DirectoryGlobMatcherTests : IDisposable
{
    readonly string _root;

    public DirectoryGlobMatcherTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"unilyze-glob-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "packages", "a"));
        Directory.CreateDirectory(Path.Combine(_root, "packages", "b"));
        Directory.CreateDirectory(Path.Combine(_root, "packages", "a", "Runtime"));
        Directory.CreateDirectory(Path.Combine(_root, "nested", "deep", "Modules", "core"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Fact]
    public void Expand_SingleStar_MatchesDirectChildren()
    {
        var matches = DirectoryGlobMatcher.Expand([Path.Combine(_root, "packages", "*")]);
        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, m => m.Path.EndsWith($"{Path.DirectorySeparatorChar}a", StringComparison.Ordinal));
        Assert.Contains(matches, m => m.Path.EndsWith($"{Path.DirectorySeparatorChar}b", StringComparison.Ordinal));
    }

    [Fact]
    public void Expand_DoubleStar_MatchesNestedDirectories()
    {
        var matches = DirectoryGlobMatcher.Expand([Path.Combine(_root, "nested", "**", "Modules", "*")]);
        Assert.Single(matches);
        Assert.EndsWith($"{Path.DirectorySeparatorChar}core", matches[0].Path);
    }

    [Fact]
    public void Expand_NoMatches_ReturnsEmpty()
    {
        var matches = DirectoryGlobMatcher.Expand([Path.Combine(_root, "missing", "*")]);
        Assert.Empty(matches);
    }

    [Fact]
    public void Expand_MultiplePatterns_DeduplicatesAndSorts()
    {
        var pattern = Path.Combine(_root, "packages", "a");
        var matches = DirectoryGlobMatcher.Expand(
        [
            Path.Combine(_root, "packages", "*"),
            pattern,
        ]);
        Assert.Equal(2, matches.Count);
        Assert.Equal(matches.OrderBy(m => m.Path, StringComparer.Ordinal).ToList(), matches);
    }

    [Fact]
    public void DeriveProjectName_ReplacesSeparatorsWithDash()
    {
        var pattern = Path.Combine(_root, "packages", "*");
        var match = Path.Combine(_root, "packages", "a", "Runtime");
        var name = DirectoryGlobMatcher.DeriveProjectName(pattern, match);
        Assert.Equal("a-Runtime", name);
    }

    [Fact]
    public void DeriveProjectName_SingleSegment_UsesDirectoryName()
    {
        var pattern = Path.Combine(_root, "packages", "*");
        var match = Path.Combine(_root, "packages", "b");
        var name = DirectoryGlobMatcher.DeriveProjectName(pattern, match);
        Assert.Equal("b", name);
    }
}
