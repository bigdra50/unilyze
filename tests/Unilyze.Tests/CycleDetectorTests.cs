using System.Text.Json;
using System.Text.Json.Nodes;

namespace Unilyze.Tests;

public class CycleDetectorTests
{
    // --- Type-level cycles ---

    [Fact]
    public void SimpleTypeCycle_AB_Detected()
    {
        var deps = new TypeDependency[]
        {
            new("A", "B", DependencyKind.FieldType),
            new("B", "A", DependencyKind.FieldType),
        };

        var cycles = CycleDetector.DetectTypeCycles(deps);

        Assert.Single(cycles);
        Assert.Equal(CycleLevel.Type, cycles[0].Level);
        Assert.Equal(2, cycles[0].Cycle.Count);
        Assert.Contains("A", cycles[0].Cycle);
        Assert.Contains("B", cycles[0].Cycle);
    }

    [Fact]
    public void ThreeNodeCycle_ABC_Detected()
    {
        var deps = new TypeDependency[]
        {
            new("A", "B", DependencyKind.FieldType),
            new("B", "C", DependencyKind.FieldType),
            new("C", "A", DependencyKind.FieldType),
        };

        var cycles = CycleDetector.DetectTypeCycles(deps);

        Assert.Single(cycles);
        Assert.Equal(3, cycles[0].Cycle.Count);
        Assert.Contains("A", cycles[0].Cycle);
        Assert.Contains("B", cycles[0].Cycle);
        Assert.Contains("C", cycles[0].Cycle);
    }

    [Fact]
    public void NoCycle_ReturnsEmpty()
    {
        var deps = new TypeDependency[]
        {
            new("A", "B", DependencyKind.Inheritance),
            new("B", "C", DependencyKind.FieldType),
        };

        var cycles = CycleDetector.DetectTypeCycles(deps);

        Assert.Empty(cycles);
    }

    [Fact]
    public void MultipleCycles_AllDetected()
    {
        var deps = new TypeDependency[]
        {
            new("A", "B", DependencyKind.FieldType),
            new("B", "A", DependencyKind.FieldType),
            new("X", "Y", DependencyKind.FieldType),
            new("Y", "X", DependencyKind.FieldType),
        };

        var cycles = CycleDetector.DetectTypeCycles(deps);

        Assert.Equal(2, cycles.Count);
    }

    [Fact]
    public void SelfReference_Excluded()
    {
        // BuildDependencies already filters self-references,
        // but even if one slips through, size-1 SCC should be excluded
        var deps = new TypeDependency[]
        {
            new("A", "A", DependencyKind.FieldType),
        };

        var cycles = CycleDetector.DetectTypeCycles(deps);

        Assert.Empty(cycles);
    }

    // --- Assembly-level cycles ---

    [Fact]
    public void AssemblyCycle_Detected()
    {
        var assemblies = new AssemblyInfo[]
        {
            new("App.Domain", "/dir", ["App.Infra"], new AssemblyMetrics("", 0, 0, 0, 0, 0, 0, 0, 0, 0, [])),
            new("App.Infra", "/dir", ["App.Domain"], new AssemblyMetrics("", 0, 0, 0, 0, 0, 0, 0, 0, 0, [])),
        };

        var cycles = CycleDetector.DetectAssemblyCycles(assemblies);

        Assert.Single(cycles);
        Assert.Equal(CycleLevel.Assembly, cycles[0].Level);
        Assert.Contains("App.Domain", cycles[0].Cycle);
        Assert.Contains("App.Infra", cycles[0].Cycle);
    }

    [Fact]
    public void AssemblyNoCycle_ReturnsEmpty()
    {
        var assemblies = new AssemblyInfo[]
        {
            new("App.Domain", "/dir", [], new AssemblyMetrics("", 0, 0, 0, 0, 0, 0, 0, 0, 0, [])),
            new("App.Infra", "/dir", ["App.Domain"], new AssemblyMetrics("", 0, 0, 0, 0, 0, 0, 0, 0, 0, [])),
        };

        var cycles = CycleDetector.DetectAssemblyCycles(assemblies);

        Assert.Empty(cycles);
    }

    // --- Tarjan SCC edge cases ---

    [Fact]
    public void TarjanSCC_EmptyGraph()
    {
        var adjacency = new Dictionary<string, List<string>>();

        var sccs = CycleDetector.TarjanSCC(adjacency);

        Assert.Empty(sccs);
    }

    [Fact]
    public void TarjanSCC_SingleNode()
    {
        var adjacency = new Dictionary<string, List<string>>
        {
            ["A"] = []
        };

        var sccs = CycleDetector.TarjanSCC(adjacency);

        Assert.Empty(sccs);
    }

    // --- DetectAll ---

    [Fact]
    public void DetectAll_CombinesTypesAndAssemblies()
    {
        var deps = new TypeDependency[]
        {
            new("A", "B", DependencyKind.FieldType),
            new("B", "A", DependencyKind.FieldType),
        };
        var assemblies = new AssemblyInfo[]
        {
            new("Asm1", "/dir", ["Asm2"], new AssemblyMetrics("", 0, 0, 0, 0, 0, 0, 0, 0, 0, [])),
            new("Asm2", "/dir", ["Asm1"], new AssemblyMetrics("", 0, 0, 0, 0, 0, 0, 0, 0, 0, [])),
        };

        var cycles = CycleDetector.DetectAll(deps, assemblies);

        Assert.Equal(2, cycles.Count);
        Assert.Contains(cycles, c => c.Level == CycleLevel.Type);
        Assert.Contains(cycles, c => c.Level == CycleLevel.Assembly);
    }

    // --- JSON serialization ---

    [Fact]
    public void JsonSerialization_CyclicDependency()
    {
        var result = new AnalysisResult(
            "/project", DateTimeOffset.UtcNow, [], [], [],
            CyclicDependencies: [
                new CyclicDependency(["A", "B"], CycleLevel.Type)
            ]);

        var json = JsonSerializer.Serialize(result, AnalysisJsonContext.Default.AnalysisResult);
        var doc = JsonNode.Parse(json)!;

        var cycles = doc["cyclicDependencies"]!.AsArray();
        Assert.Single(cycles);
        Assert.Equal("Type", cycles[0]!["level"]!.GetValue<string>());
        var cycle = cycles[0]!["cycle"]!.AsArray();
        Assert.Equal(2, cycle.Count);
    }
}
