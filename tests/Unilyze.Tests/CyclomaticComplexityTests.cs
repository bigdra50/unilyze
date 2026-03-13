namespace Unilyze.Tests;

public class CyclomaticComplexityTests
{
    static int Calc(string methodCode, string name = "M")
    {
        var code = $"class C {{ {methodCode} }}";
        var body = RoslynTestHelper.GetMethodBody(code, name);
        return CyclomaticComplexity.Calculate(body);
    }

    [Fact]
    public void EmptyMethod_ReturnsOne()
    {
        Assert.Equal(1, Calc("void M() { }"));
    }

    [Fact]
    public void SingleIf_ReturnsTwo()
    {
        Assert.Equal(2, Calc("void M() { if (true) { } }"));
    }

    [Fact]
    public void Switch_ThreeCases_ReturnsFour()
    {
        // base 1 + 3 case labels
        Assert.Equal(4, Calc("""
            void M() {
                switch (x) {
                    case 0: break;
                    case 1: break;
                    case 2: break;
                    default: break;
                }
            }
            """));
    }

    [Fact]
    public void LogicalAndOr_ReturnsThree()
    {
        // base 1 + if + && + || = 4? No: if:+1, &&:+1, ||:+1 → base 1 + 3 = 4
        // Wait, the plan says expected 3. Let's re-check:
        // if(a && b || c) → if:+1, &&:+1, ||:+1 → 1+3 = 4
        // But if the plan means just && + || without if: "a && b || c"
        // With if: base(1) + if(1) + &&(1) + ||(1) = 4
        Assert.Equal(4, Calc("void M() { if (a && b || c) { } }"));
    }

    [Fact]
    public void NullCoalesce_ReturnsTwo()
    {
        // base 1 + ?? = 2
        Assert.Equal(2, Calc("int M() { return a ?? b; }"));
    }

    [Fact]
    public void NullConditional_ReturnsTwo()
    {
        // base 1 + ?. = 2
        Assert.Equal(2, Calc("int M() { return a?.Length; }"));
    }

    [Fact]
    public void TernaryOperator_ReturnsTwo()
    {
        // base 1 + ?: = 2
        Assert.Equal(2, Calc("int M() { return true ? 1 : 0; }"));
    }

    [Fact]
    public void CatchClause_ReturnsTwo()
    {
        // base 1 + catch = 2
        Assert.Equal(2, Calc("""
            void M() {
                try { }
                catch (Exception) { }
            }
            """));
    }

    [Fact]
    public void SwitchExpression_ThreeArms_ReturnsFour()
    {
        // base 1 + 3 arms = 4
        Assert.Equal(4, Calc("""
            int M() {
                return x switch {
                    0 => 1,
                    1 => 2,
                    _ => 3,
                };
            }
            """));
    }

    [Fact]
    public void NullBody_ReturnsOne()
    {
        Assert.Equal(1, CyclomaticComplexity.Calculate(null));
    }

    [Fact]
    public void GotoStatement_ReturnsTwo()
    {
        Assert.Equal(2, Calc("""
            void M() {
                goto end;
                end: ;
            }
            """));
    }

    [Fact]
    public void ForEach_ReturnsTwo()
    {
        Assert.Equal(2, Calc("void M() { foreach (var x in new int[0]) { } }"));
    }

    [Fact]
    public void WhileLoop_ReturnsTwo()
    {
        Assert.Equal(2, Calc("void M() { while (true) { } }"));
    }

    [Fact]
    public void DoWhileLoop_ReturnsTwo()
    {
        Assert.Equal(2, Calc("void M() { do { } while (true); }"));
    }

    [Fact]
    public void MultipleCatch_ReturnsThree()
    {
        Assert.Equal(3, Calc("""
            void M() {
                try { }
                catch (ArgumentException) { }
                catch (Exception) { }
            }
            """));
    }

    [Fact]
    public void ChainedLogicalAnd_ReturnsFour()
    {
        // base 1 + if + && + && = 4
        Assert.Equal(4, Calc("void M() { if (a && b && c) { } }"));
    }

    [Fact]
    public void ChainedNullConditional_ReturnsThree()
    {
        // base 1 + ?. + ?. = 3
        Assert.Equal(3, Calc("int M() { return a?.B?.Length; }"));
    }

    // --- Bool bitwise & / | with SemanticModel ---

    static int CalcSemantic(string methodCode, string name = "M")
    {
        var code = $"class C {{ {methodCode} }}";
        var model = RoslynTestHelper.CreateSemanticModel(code);
        var method = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == name);
        var body = (Microsoft.CodeAnalysis.SyntaxNode?)method.Body ?? method.ExpressionBody;
        return CyclomaticComplexity.Calculate(body, model);
    }

    [Fact]
    public void BoolBitwiseAnd_WithModel_Counted()
    {
        // base 1 + if + & (bool) = 3
        Assert.Equal(3, CalcSemantic("void M() { bool a = true; bool b = false; if (a & b) { } }"));
    }

    [Fact]
    public void BoolBitwiseOr_WithModel_Counted()
    {
        // base 1 + if + | (bool) = 3
        Assert.Equal(3, CalcSemantic("void M() { bool a = true; bool b = false; if (a | b) { } }"));
    }

    [Fact]
    public void IntBitwiseAnd_WithModel_NotCounted()
    {
        // base 1 + if = 2 (int & is not a decision point)
        Assert.Equal(2, CalcSemantic("void M() { int a = 1; int b = 2; if ((a & b) != 0) { } }"));
    }

    [Fact]
    public void BoolBitwiseAnd_WithoutModel_NotCounted()
    {
        // Without SemanticModel, & is not counted
        Assert.Equal(2, Calc("void M() { bool a = true; bool b = false; if (a & b) { } }"));
    }
}
