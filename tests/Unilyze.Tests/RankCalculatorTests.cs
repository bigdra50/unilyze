namespace Unilyze.Tests;

public class RankCalculatorTests
{
    static TypeNodeInfo MakeType(string name, string ns = "TestNs")
    {
        var typeId = $"{ns}.{name}";
        return new TypeNodeInfo(
            name, ns, "class", [], null, [], [], [], [], [], null,
            "TestAssembly", "test.cs", false, 10, 1, typeId, typeId);
    }

    static TypeDependency MakeDep(string from, string to, string ns = "TestNs")
    {
        return new TypeDependency(from, to, DependencyKind.FieldType,
            $"{ns}.{from}", $"{ns}.{to}");
    }

    [Fact]
    public void EmptyInput_ReturnsEmptyDictionary()
    {
        var result = RankCalculator.CalculateTypeRank([], []);
        Assert.Empty(result);
    }

    [Fact]
    public void IsolatedNodes_EqualRank()
    {
        var types = new[] { MakeType("A"), MakeType("B"), MakeType("C") };
        var result = RankCalculator.CalculateTypeRank([], types);

        Assert.Equal(3, result.Count);
        double expected = 1.0 / 3;
        foreach (var kvp in result)
            Assert.Equal(expected, kvp.Value, precision: 5);
    }

    [Fact]
    public void StarGraph_LeavesHigherThanCenter()
    {
        // A -> B, A -> C, A -> D
        var types = new[] { MakeType("A"), MakeType("B"), MakeType("C"), MakeType("D") };
        var deps = new[]
        {
            MakeDep("A", "B"),
            MakeDep("A", "C"),
            MakeDep("A", "D")
        };

        var result = RankCalculator.CalculateTypeRank(deps, types);

        var rankA = result["TestNs.A"];
        var rankB = result["TestNs.B"];
        var rankC = result["TestNs.C"];
        var rankD = result["TestNs.D"];

        // Leaves receive links, so they should have higher rank than the center
        Assert.True(rankB > rankA, $"B ({rankB}) should be > A ({rankA})");
        Assert.True(rankC > rankA, $"C ({rankC}) should be > A ({rankA})");
        Assert.True(rankD > rankA, $"D ({rankD}) should be > A ({rankA})");

        // All leaves should have equal rank
        Assert.Equal(rankB, rankC, precision: 5);
        Assert.Equal(rankC, rankD, precision: 5);
    }

    [Fact]
    public void Chain_LastNodeHighestRank()
    {
        // A -> B -> C
        var types = new[] { MakeType("A"), MakeType("B"), MakeType("C") };
        var deps = new[]
        {
            MakeDep("A", "B"),
            MakeDep("B", "C")
        };

        var result = RankCalculator.CalculateTypeRank(deps, types);

        var rankA = result["TestNs.A"];
        var rankB = result["TestNs.B"];
        var rankC = result["TestNs.C"];

        // C should have the highest rank (end of chain, receives link from B)
        Assert.True(rankC > rankB, $"C ({rankC}) should be > B ({rankB})");
        Assert.True(rankB > rankA, $"B ({rankB}) should be > A ({rankA})");
    }

    [Fact]
    public void CompleteGraph_AllNodesEqualRank()
    {
        // A -> B, A -> C, B -> A, B -> C, C -> A, C -> B
        var types = new[] { MakeType("A"), MakeType("B"), MakeType("C") };
        var deps = new[]
        {
            MakeDep("A", "B"), MakeDep("A", "C"),
            MakeDep("B", "A"), MakeDep("B", "C"),
            MakeDep("C", "A"), MakeDep("C", "B")
        };

        var result = RankCalculator.CalculateTypeRank(deps, types);

        double expected = 1.0 / 3;
        foreach (var kvp in result)
            Assert.Equal(expected, kvp.Value, precision: 5);
    }

    [Fact]
    public void RanksNormalizeToOne()
    {
        var types = new[] { MakeType("A"), MakeType("B"), MakeType("C"), MakeType("D") };
        var deps = new[]
        {
            MakeDep("A", "B"),
            MakeDep("A", "C"),
            MakeDep("B", "D"),
            MakeDep("C", "D")
        };

        var result = RankCalculator.CalculateTypeRank(deps, types);

        double sum = result.Values.Sum();
        Assert.Equal(1.0, sum, precision: 5);
    }
}
