using System.Text.Json;

namespace Unilyze.Tests.Incremental;

public sealed class SyntaxCacheStoreTests : IDisposable
{
    readonly string _projectRoot;

    public SyntaxCacheStoreTests()
    {
        _projectRoot = Path.Combine(Path.GetTempPath(), $"unilyze-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectRoot))
            Directory.Delete(_projectRoot, true);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsManifest()
    {
        var manifest = SampleManifest("abc123");
        SyntaxCacheStore.Save(_projectRoot, manifest);

        var loaded = SyntaxCacheStore.TryLoad(_projectRoot, "abc123");
        Assert.NotNull(loaded);
        Assert.Equal(manifest.SchemaVersion, loaded!.SchemaVersion);
        Assert.Equal(manifest.Fingerprint, loaded.Fingerprint);
        Assert.Single(loaded.Files);
        Assert.Equal("Foo.cs", loaded.Files[0].RelativePath);
    }

    [Fact]
    public void TryLoad_ReturnsNullWhenFingerprintMismatch()
    {
        SyntaxCacheStore.Save(_projectRoot, SampleManifest("expected"));
        Assert.Null(SyntaxCacheStore.TryLoad(_projectRoot, "other"));
    }

    [Fact]
    public void TryLoad_ReturnsNullWhenSchemaVersionMismatch()
    {
        var path = SyntaxCacheStore.GetManifestPath(_projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"schemaVersion":999,"fingerprint":"x","knownInterfacesHashesByAssembly":{},"files":[]}""");

        Assert.Null(SyntaxCacheStore.TryLoad(_projectRoot, "x"));
    }

    [Fact]
    public void TryLoad_ReturnsNullForCorruptJson()
    {
        var path = SyntaxCacheStore.GetManifestPath(_projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{not-json");

        Assert.Null(SyntaxCacheStore.TryLoad(_projectRoot, "x"));
    }

    [Fact]
    public void EnsureGitIgnore_CreatesStarFile()
    {
        SyntaxCacheStore.EnsureGitIgnore(_projectRoot);
        var gitIgnore = Path.Combine(_projectRoot, ".unilyze", "cache", ".gitignore");
        Assert.True(File.Exists(gitIgnore));
        Assert.Equal("*\n", File.ReadAllText(gitIgnore));
    }

    static SyntaxCacheManifest SampleManifest(string fingerprint) =>
        new(
            SyntaxCacheFingerprint.SchemaVersion,
            fingerprint,
            new Dictionary<string, string> { ["Asm"] = "deadbeef" },
            [
                new SyntaxCacheFileEntry(
                    "Foo.cs",
                    "hash",
                    "Asm",
                    [],
                    [])
            ]);
}
