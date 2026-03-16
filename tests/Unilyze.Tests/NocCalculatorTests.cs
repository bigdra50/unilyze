namespace Unilyze.Tests;

public class NocCalculatorTests
{
    [Fact]
    public void NoInheritance_Empty()
    {
        var deps = new List<TypeDependency>
        {
            new("A", "IFoo", DependencyKind.InterfaceImpl, "A_id", "IFoo_id"),
            new("B", "IBar", DependencyKind.InterfaceImpl, "B_id", "IBar_id"),
        };
        var result = NocCalculator.Calculate(deps);
        Assert.Empty(result);
    }

    [Fact]
    public void TwoChildren_ParentHasTwo()
    {
        // A <- B, A <- C
        var deps = new List<TypeDependency>
        {
            new("B", "A", DependencyKind.Inheritance, "B_id", "A_id"),
            new("C", "A", DependencyKind.Inheritance, "C_id", "A_id"),
        };
        var result = NocCalculator.Calculate(deps);
        Assert.Equal(2, result["A_id"]);
        Assert.False(result.ContainsKey("B_id"));
        Assert.False(result.ContainsKey("C_id"));
    }

    [Fact]
    public void MultiLevelInheritance_DirectChildrenOnly()
    {
        // A <- B <- C (direct children only)
        var deps = new List<TypeDependency>
        {
            new("B", "A", DependencyKind.Inheritance, "B_id", "A_id"),
            new("C", "B", DependencyKind.Inheritance, "C_id", "B_id"),
        };
        var result = NocCalculator.Calculate(deps);
        Assert.Equal(1, result["A_id"]);
        Assert.Equal(1, result["B_id"]);
        Assert.False(result.ContainsKey("C_id"));
    }

    [Fact]
    public void MixedDependencyKinds_OnlyInheritanceCounted()
    {
        var deps = new List<TypeDependency>
        {
            new("B", "A", DependencyKind.Inheritance, "B_id", "A_id"),
            new("C", "A", DependencyKind.FieldType, "C_id", "A_id"),
            new("D", "A", DependencyKind.MethodParam, "D_id", "A_id"),
        };
        var result = NocCalculator.Calculate(deps);
        Assert.Equal(1, result["A_id"]);
    }
}
