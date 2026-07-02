using Unilyze.Incremental;
using Unilyze.Metrics;

namespace Unilyze.Tests.Incremental;

public sealed class ReverseDependencyIndexTests
{
    [Fact]
    public void Build_InvertsUsedTypesIntoRDeps()
    {
        var files = new[]
        {
            FileWith(("T1", ["B", "C"]), ("T2", ["B"])),
            FileWith(("T3", ["C"])),
        };

        var rdeps = ReverseDependencyIndex.Build(files);

        Assert.Equal(["T1", "T2"], ReverseDependencyIndex.Resolve(rdeps, "B").Order(StringComparer.Ordinal));
        Assert.Equal(["T1", "T3"], ReverseDependencyIndex.Resolve(rdeps, "C").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Resolve_ReturnsEmptyForANeverUsedType()
    {
        var files = new[] { FileWith(("T1", ["B"])) };
        var rdeps = ReverseDependencyIndex.Build(files);

        Assert.Empty(ReverseDependencyIndex.Resolve(rdeps, "Unreferenced"));
    }

    [Fact]
    public void ResolveMany_UnionsRDepsAcrossMultipleSeeds()
    {
        var files = new[] { FileWith(("T1", ["B"]), ("T2", ["C"]), ("T3", ["B", "C"])) };
        var rdeps = ReverseDependencyIndex.Build(files);

        var result = ReverseDependencyIndex.ResolveMany(rdeps, ["B", "C"]);

        Assert.Equal(new HashSet<string> { "T1", "T2", "T3" }, result);
    }

    [Fact]
    public void Build_EmptyManifest_ProducesEmptyIndex()
    {
        var rdeps = ReverseDependencyIndex.Build([]);

        Assert.Empty(ReverseDependencyIndex.Resolve(rdeps, "AnyType"));
    }

    static SyntaxCacheFileEntry FileWith(params (string TypeId, string[] UsedTypes)[] types) =>
        new("Fixture.cs", "hash", "Asm", [],
            types.Select(t => new SyntaxCacheEnrichedType(t.TypeId, SampleMetrics(t.TypeId), t.UsedTypes)).ToList());

    static TypeMetrics SampleMetrics(string typeId) =>
        new(typeId, "Sample", "Asm", 1, 0, 0, 0, 0, 0, 0, 0, 100, []);
}
