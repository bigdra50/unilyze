using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests;

public class RfcCalculatorTests
{
    static int Calc(string code, string typeName = "C")
    {
        var typeDecl = RoslynTestHelper.GetType(code, typeName);
        return RfcCalculator.Calculate(typeDecl, model: null);
    }

    static int CalcSemantic(string code, string typeName = "C")
    {
        var model = RoslynTestHelper.CreateSemanticModel(code);
        var typeDecl = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == typeName);
        return RfcCalculator.Calculate(typeDecl, model);
    }

    // --- Syntactic tests ---

    [Fact]
    public void EmptyClass_Zero()
    {
        Assert.Equal(0, Calc("class C { }"));
    }

    [Fact]
    public void ThreeMethods_NoExternalCalls()
    {
        var code = """
            class C {
                void A() { }
                void B() { }
                void C1() { }
            }
            """;
        Assert.Equal(3, Calc(code));
    }

    [Fact]
    public void TwoMethods_ThreeExternalCalls()
    {
        var code = """
            class Other {
                public void X() { }
                public void Y() { }
                public void Z() { }
            }
            class C {
                Other _o;
                void A() { _o.X(); _o.Y(); }
                void B() { _o.Z(); }
            }
            """;
        // M=2, R=3 unique external methods (X, Y, Z)
        Assert.Equal(5, Calc(code));
    }

    [Fact]
    public void DuplicateExternalCalls_NotDoubleCounted()
    {
        var code = """
            class Other {
                public void X() { }
            }
            class C {
                Other _o;
                void A() { _o.X(); _o.X(); _o.X(); }
                void B() { _o.X(); }
            }
            """;
        // M=2, R=1 unique external method (X)
        Assert.Equal(3, Calc(code));
    }

    [Fact]
    public void ConstructorsCountAsM()
    {
        var code = """
            class C {
                public C() { }
                public C(int x) { }
                void Foo() { }
            }
            """;
        // M=3 (2 constructors + 1 method), R=0
        Assert.Equal(3, Calc(code));
    }

    [Fact]
    public void SelfMethodCalls_IncludedInR()
    {
        var code = """
            class C {
                void A() { B(); }
                void B() { }
            }
            """;
        // M=2, R=1 (B is invoked; RFC includes self-calls per definition)
        Assert.Equal(3, Calc(code));
    }

    // --- Semantic tests ---

    [Fact]
    public void Semantic_EmptyClass_Zero()
    {
        Assert.Equal(0, CalcSemantic("class C { }"));
    }

    [Fact]
    public void Semantic_ThreeMethods_NoExternalCalls()
    {
        var code = """
            class C {
                void A() { }
                void B() { }
                void D() { }
            }
            """;
        Assert.Equal(3, CalcSemantic(code));
    }

    [Fact]
    public void Semantic_TwoMethods_ThreeExternalCalls()
    {
        var code = """
            class Other {
                public void X() { }
                public void Y() { }
                public void Z() { }
            }
            class C {
                Other _o = new Other();
                void A() { _o.X(); _o.Y(); }
                void B() { _o.Z(); }
            }
            """;
        Assert.Equal(5, CalcSemantic(code));
    }

    [Fact]
    public void Semantic_DuplicateExternalCalls_NotDoubleCounted()
    {
        var code = """
            class Other {
                public void X() { }
            }
            class C {
                Other _o = new Other();
                void A() { _o.X(); _o.X(); _o.X(); }
                void B() { _o.X(); }
            }
            """;
        // M=2, R=1
        Assert.Equal(3, CalcSemantic(code));
    }
}
