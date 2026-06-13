namespace Unilyze.Tests.Pipeline;

public class AssemblyAggregationIndexTests
{
    static TypeNodeInfo MakeType(string name, string assembly, string kind = "class")
        => new(name, "", kind, [], null, [], [],
            [], [], [], null, assembly, "test.cs", false);

    static TypeDependency Dep(string fromId, string toId)
        => new("From", "To", DependencyKind.Inheritance, fromId, toId);

    [Fact]
    public void InternalRelationCounts_MatchAssemblyMetricsDependencyScan()
    {
        var types = new[]
        {
            MakeType("A", "Asm1"),
            MakeType("B", "Asm1"),
            MakeType("C", "Asm2"),
        };
        var aId = TypeIdentity.GetTypeId(types[0]);
        var bId = TypeIdentity.GetTypeId(types[1]);
        var cId = TypeIdentity.GetTypeId(types[2]);

        var deps = new[]
        {
            Dep(aId, bId),
            Dep(aId, bId),
            Dep(aId, cId),
            Dep(bId, aId),
        };

        var index = AssemblyAggregationIndex.Build(types, deps);
        var asm1Types = types.Where(t => t.Assembly == "Asm1").ToList();
        var asmDeps = deps.Where(d =>
            d.FromTypeId == aId || d.FromTypeId == bId || d.ToTypeId == aId || d.ToTypeId == bId).ToList();

        var fromIndex = AssemblyMetrics.Compute(
            "Asm1", asm1Types, couplingMap: null, internalRelationCount: index.InternalRelationCounts["Asm1"]);
        var fromDeps = AssemblyMetrics.Compute("Asm1", asm1Types, asmDeps);

        Assert.Equal(fromDeps.RelationalCohesion, fromIndex.RelationalCohesion);
    }

    [Fact]
    public void TypesByAssembly_GroupsAllTypes()
    {
        var types = new[]
        {
            MakeType("A", "Asm1"),
            MakeType("B", "Asm1"),
            MakeType("C", "Asm2"),
        };

        var index = AssemblyAggregationIndex.Build(types, []);

        Assert.Equal(2, index.TypesByAssembly["Asm1"].Count);
        Assert.Single(index.TypesByAssembly["Asm2"]);
    }

    [Fact]
    public void IndexedAssemblyMetrics_MatchLegacyDependencyFilterPath()
    {
        var types = new[]
        {
            MakeType("A", "Asm1"),
            MakeType("B", "Asm1"),
            MakeType("C", "Asm2"),
            MakeType("D", "Asm2"),
        };
        var ids = types.Select(TypeIdentity.GetTypeId).ToArray();
        var deps = new[]
        {
            Dep(ids[0], ids[1]),
            Dep(ids[1], ids[0]),
            Dep(ids[0], ids[2]),
            Dep(ids[3], ids[2]),
        };
        var couplingMap = CouplingMetricsCalculator.Calculate(deps, types);
        var index = AssemblyAggregationIndex.Build(types, deps);

        foreach (var assembly in new[] { "Asm1", "Asm2" })
        {
            var asmTypes = types.Where(t => t.Assembly == assembly).ToList();
            var asmDeps = deps.Where(d =>
                types.Any(t => TypeIdentity.GetTypeId(t) == d.FromTypeId && t.Assembly == assembly) ||
                types.Any(t => TypeIdentity.GetTypeId(t) == d.ToTypeId && t.Assembly == assembly)).ToList();

            var legacy = AssemblyMetrics.Compute(assembly, asmTypes, asmDeps, couplingMap);
            var indexed = AssemblyMetrics.Compute(
                assembly,
                index.TypesByAssembly[assembly],
                couplingMap: couplingMap,
                internalRelationCount: index.InternalRelationCounts.GetValueOrDefault(assembly));

            Assert.Equal(legacy.AssemblyName, indexed.AssemblyName);
            Assert.Equal(legacy.TypeCount, indexed.TypeCount);
            Assert.Equal(legacy.Abstractness, indexed.Abstractness);
            Assert.Equal(legacy.DistanceFromMainSequence, indexed.DistanceFromMainSequence);
            Assert.Equal(legacy.RelationalCohesion, indexed.RelationalCohesion);
        }
    }
}
