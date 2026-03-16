namespace Unilyze.Tests;

public class HalsteadDerivedMetricsTests
{
    static HalsteadMetrics CalcHalstead(string methodCode, string name = "M")
    {
        var code = $"class C {{ {methodCode} }}";
        var body = RoslynTestHelper.GetMethodBody(code, name);
        return HalsteadCalculator.Calculate(body);
    }

    [Fact]
    public void NullBody_AllDerivedZero()
    {
        var result = HalsteadCalculator.Calculate(null);
        Assert.Equal(0, result.Difficulty);
        Assert.Equal(0, result.Effort);
        Assert.Equal(0, result.EstimatedBugs);
    }

    [Fact]
    public void EmptyMethod_AllDerivedZero()
    {
        var result = CalcHalstead("void M() { }");
        Assert.Equal(0, result.Difficulty);
        Assert.Equal(0, result.Effort);
        Assert.Equal(0, result.EstimatedBugs);
    }

    [Fact]
    public void SimpleAssignment_PositiveDerived()
    {
        var result = CalcHalstead("void M() { var x = 1; }");
        Assert.True(result.Difficulty > 0);
        Assert.True(result.Effort > 0);
        Assert.True(result.EstimatedBugs > 0);
    }

    [Fact]
    public void Difficulty_Formula()
    {
        // Difficulty = (UniqueOperators / 2.0) * (TotalOperands / UniqueOperands)
        var result = CalcHalstead("int M() { var r = a + b * c; return r; }");
        var expected = (result.UniqueOperators / 2.0) * ((double)result.TotalOperands / result.UniqueOperands);
        Assert.Equal(expected, result.Difficulty);
    }

    [Fact]
    public void Effort_IsDifficultyTimesVolume()
    {
        var result = CalcHalstead("int M() { var r = a + b * c; return r; }");
        Assert.Equal(result.Difficulty * result.Volume, result.Effort, 6);
    }

    [Fact]
    public void EstimatedBugs_Formula()
    {
        var result = CalcHalstead("int M() { var r = a + b * c; return r; }");
        var expected = Math.Pow(result.Effort, 2.0 / 3.0) / 3000.0;
        Assert.Equal(expected, result.EstimatedBugs, 6);
    }

    [Fact]
    public void ComplexMethod_HigherEffort()
    {
        var simple = CalcHalstead("int M() { return 1; }");
        var complex = CalcHalstead("""
            int M() {
                var sum = 0;
                for (var i = 0; i < 10; i++) {
                    if (i % 2 == 0) { sum += i; }
                    else { sum -= i; }
                }
                return sum;
            }
            """);
        Assert.True(complex.Effort > simple.Effort);
        Assert.True(complex.EstimatedBugs > simple.EstimatedBugs);
    }
}
