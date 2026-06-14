using Unilyze.Pipeline;

namespace Unilyze.Tests.Serve;

public sealed class ServeSourceBoundaryTests : IDisposable
{
    readonly string _root;
    readonly string _outside;

    public ServeSourceBoundaryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"unilyze-root-{Guid.NewGuid():N}");
        _outside = Path.Combine(Path.GetTempPath(), $"unilyze-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        TryDelete(_root);
        TryDelete(_outside);
    }

    [Fact]
    public void Sanitize_RootOutsideFile_DoesNotAddSourceAllowlistEntry()
    {
        var outsideFile = Path.Combine(_outside, "Secret.cs");
        File.WriteAllText(outsideFile, "class Secret {}");
        var result = CreateResult(outsideFile);

        var actual = SnapshotSanitizer.Sanitize(result, [_root]);

        Assert.Empty(actual.FileIdToAbsolutePath);
        Assert.Equal(string.Empty, actual.Result.Types.Single().FilePath);
    }

    [Fact]
    public void ResolveAllowedFile_SymlinkRetargetedOutsideRoot_IsRejected()
    {
        if (OperatingSystem.IsWindows())
            return;

        var insideFile = Path.Combine(_root, "Source.cs");
        var outsideFile = Path.Combine(_outside, "Secret.cs");
        File.WriteAllText(insideFile, "class Source {}");
        File.WriteAllText(outsideFile, "class Secret {}");
        var roots = SourcePathBoundary.ResolveAllowedRoots([_root]);
        Assert.True(SourcePathBoundary.TryResolveAllowedFile(insideFile, roots, out _));

        File.Delete(insideFile);
        File.CreateSymbolicLink(insideFile, outsideFile);

        Assert.False(SourcePathBoundary.TryResolveAllowedFile(insideFile, roots, out _));
    }

    static AnalysisResult CreateResult(string filePath)
    {
        var type = new TypeNodeInfo(
            "Secret",
            "Example",
            "class",
            [],
            null,
            [],
            [],
            [],
            [],
            [],
            null,
            "External",
            filePath,
            false);
        return new AnalysisResult(
            filePath,
            DateTimeOffset.UtcNow,
            [],
            [type],
            []);
    }

    static void TryDelete(string path)
    {
        try { Directory.Delete(path, true); } catch { /* best effort */ }
    }
}
