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

    // UsedTypes(T) round-trip (design doc §4.2): the recorded set must survive a save/reload
    // cycle intact, ordinal-sorted, since Phase A2's RDeps inversion reads it back from disk.
    [Fact]
    public void SaveAndLoad_RoundTripsUsedTypes()
    {
        var metrics = new TypeMetrics("Foo", "Sample", "Asm", 10, 1, 0, 0, 0, 0, 0, 0, 100, []);
        var enriched = new SyntaxCacheEnrichedType(
            "Asm::Sample.Foo", metrics, ["Asm::Sample.Bar", "Asm::Sample.Baz"]);
        var manifest = SampleManifest("used-types") with
        {
            Files = [new SyntaxCacheFileEntry("Foo.cs", "hash", "Asm", [], [enriched])]
        };

        SyntaxCacheStore.Save(_projectRoot, manifest);
        var loaded = SyntaxCacheStore.TryLoad(_projectRoot, "used-types");

        Assert.NotNull(loaded);
        var loadedEnriched = Assert.Single(loaded!.Files[0].EnrichedTypes);
        Assert.Equal(["Asm::Sample.Bar", "Asm::Sample.Baz"], loadedEnriched.UsedTypes);
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
