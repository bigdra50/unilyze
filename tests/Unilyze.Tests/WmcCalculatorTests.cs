namespace Unilyze.Tests;

public class WmcCalculatorTests
{
    [Fact]
    public void EmptyMembers_ReturnsZero()
    {
        var members = new List<MemberInfo>();
        Assert.Equal(0, WmcCalculator.Calculate(members));
    }

    [Fact]
    public void ThreeMethods_SumsCyclomaticComplexity()
    {
        var members = new List<MemberInfo>
        {
            new("M1", "void", "Method", [], [], [], CyclomaticComplexity: 1),
            new("M2", "void", "Method", [], [], [], CyclomaticComplexity: 5),
            new("M3", "void", "Method", [], [], [], CyclomaticComplexity: 3),
        };
        Assert.Equal(9, WmcCalculator.Calculate(members));
    }

    [Fact]
    public void NullCyclomaticComplexity_Skipped()
    {
        var members = new List<MemberInfo>
        {
            new("M1", "void", "Method", [], [], [], CyclomaticComplexity: 2),
            new("Prop", "int", "Property", [], [], []),
            new("M2", "void", "Method", [], [], [], CyclomaticComplexity: 4),
            new("Field", "string", "Field", [], [], []),
        };
        Assert.Equal(6, WmcCalculator.Calculate(members));
    }
}
