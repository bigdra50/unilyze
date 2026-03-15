namespace Unilyze.Tests;

public class NestingDepthTests
{
    static int Calc(string methodCode, string name = "M")
    {
        var code = $"class C {{ {methodCode} }}";
        var body = RoslynTestHelper.GetMethodBody(code, name);
        return NestingDepth.Calculate(body);
    }

    [Fact]
    public void EmptyMethod_ReturnsZero()
    {
        Assert.Equal(0, Calc("void M() { }"));
    }

    [Fact]
    public void SingleIf_ReturnsOne()
    {
        Assert.Equal(1, Calc("void M() { if (true) { } }"));
    }

    [Fact]
    public void NestedIf_ReturnsTwo()
    {
        Assert.Equal(2, Calc("""
            void M() {
                if (true) {
                    if (false) { }
                }
            }
            """));
    }

    [Fact]
    public void ForWithIf_ReturnsTwo()
    {
        Assert.Equal(2, Calc("""
            void M() {
                for (int i = 0; i < 10; i++) {
                    if (true) { }
                }
            }
            """));
    }

    [Fact]
    public void LambdaWithIf_ReturnsTwo()
    {
        Assert.Equal(2, Calc("""
            void M() {
                Action a = () => {
                    if (true) { }
                };
            }
            """));
    }

    [Fact]
    public void Switch_ReturnsOne()
    {
        Assert.Equal(1, Calc("""
            void M() {
                switch (x) {
                    case 0: break;
                }
            }
            """));
    }

    [Fact]
    public void TryCatch_ReturnsOne()
    {
        Assert.Equal(1, Calc("""
            void M() {
                try { }
                catch (Exception) { }
            }
            """));
    }

    [Fact]
    public void ElseIf_FlatChain_ReturnsOne()
    {
        Assert.Equal(1, Calc("""
            void M() {
                if (true) { }
                else if (false) { }
                else if (true) { }
                else { }
            }
            """));
    }

    [Fact]
    public void ElseIf_WithNestedIf_ReturnsTwo()
    {
        Assert.Equal(2, Calc("""
            void M() {
                if (true) {
                    if (false) { }
                }
                else if (false) { }
                else { }
            }
            """));
    }

    [Fact]
    public void Else_WithNestedIf_ReturnsTwo()
    {
        // else { if (...) } is genuine nesting, unlike else if
        Assert.Equal(2, Calc("""
            void M() {
                if (true) { }
                else {
                    if (false) { }
                }
            }
            """));
    }
}
