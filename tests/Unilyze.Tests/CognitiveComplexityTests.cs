namespace Unilyze.Tests;

public class CognitiveComplexityTests
{
    static int Calc(string methodCode, string name = "M")
    {
        var code = $"class C {{ {methodCode} }}";
        var body = RoslynTestHelper.GetMethodBody(code, name);
        return CognitiveComplexity.Calculate(body);
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
    public void IfElseIfElse_ReturnsThree()
    {
        // if: +1, else if: +1, else: +1
        Assert.Equal(3, Calc("""
            void M() {
                if (true) { }
                else if (false) { }
                else { }
            }
            """));
    }

    [Fact]
    public void NestedIf_TwoLevels_ReturnsThree()
    {
        // outer if: +1 (nesting=0), inner if: +1 +1(nesting) = +2
        Assert.Equal(3, Calc("""
            void M() {
                if (true) {
                    if (false) { }
                }
            }
            """));
    }

    [Fact]
    public void NestedIf_ThreeLevels_ReturnsSix()
    {
        // outer: +1, mid: +1+1=2, inner: +1+2=3 → total=6
        Assert.Equal(6, Calc("""
            void M() {
                if (true) {
                    if (false) {
                        if (true) { }
                    }
                }
            }
            """));
    }

    [Fact]
    public void ForLoop_ReturnsOne()
    {
        Assert.Equal(1, Calc("void M() { for (int i = 0; i < 10; i++) { } }"));
    }

    [Fact]
    public void SwitchStatement_ReturnsOne()
    {
        Assert.Equal(1, Calc("""
            void M() {
                switch (1) {
                    case 0: break;
                    case 1: break;
                }
            }
            """));
    }

    [Fact]
    public void LogicalAnd_Single_ReturnsOne()
    {
        Assert.Equal(2, Calc("""
            void M() {
                if (true && false) { }
            }
            """));
    }

    [Fact]
    public void LogicalAnd_Consecutive_SameKind_ReturnsOne()
    {
        // if: +1, && sequence: +1 (same operator kind, counted once)
        Assert.Equal(2, Calc("""
            void M() {
                if (a && b && c) { }
            }
            """));
    }

    [Fact]
    public void LogicalAnd_Then_Or_Mixed_ReturnsTwo()
    {
        // if: +1, &&: +1, ||: +1 (operator kind change)
        Assert.Equal(3, Calc("""
            void M() {
                if (a && b || c) { }
            }
            """));
    }

    [Fact]
    public void NullCoalesce_ReturnsZero()
    {
        // ?? is shorthand — no increment per SonarSource spec
        Assert.Equal(0, Calc("int M() { return a ?? b; }"));
    }

    [Fact]
    public void NullConditional_ReturnsZero()
    {
        // ?. is shorthand — no increment
        Assert.Equal(0, Calc("int M() { return a?.Length ?? 0; }"));
    }

    [Fact]
    public void LambdaWithIf_ReturnsTwo()
    {
        // lambda increases nesting (+1), if inside lambda: +1 structural +1 nesting = +2
        Assert.Equal(2, Calc("""
            void M() {
                Action a = () => {
                    if (true) { }
                };
            }
            """));
    }

    [Fact]
    public void Goto_ReturnsOne()
    {
        Assert.Equal(1, Calc("""
            void M() {
                goto end;
                end: ;
            }
            """));
    }

    // --- LastBinaryOp state leak regression tests ---

    [Fact]
    public void ConsecutiveIfs_SameOperator_CountedIndependently()
    {
        // Each if + && should be counted independently
        // if: +1, &&: +1, if: +1, &&: +1 = 4
        Assert.Equal(4, Calc("""
            void M() {
                if (a && b) { }
                if (c && d) { }
            }
            """));
    }

    [Fact]
    public void ConsecutiveIfs_DifferentOperators_CountedIndependently()
    {
        // if: +1, &&: +1, if: +1, ||: +1 = 4
        Assert.Equal(4, Calc("""
            void M() {
                if (a && b) { }
                if (c || d) { }
            }
            """));
    }

    [Fact]
    public void WhileAfterIf_SameOperator_CountedIndependently()
    {
        // if: +1, &&: +1, while: +1, &&: +1 = 4
        Assert.Equal(4, Calc("""
            void M() {
                if (a && b) { }
                while (c && d) { }
            }
            """));
    }

    [Fact]
    public void NestedIf_InnerCondition_Independent()
    {
        // outer if: +1(nesting=0), &&: +1, inner if: +1+1(nesting=1), &&: +1 = 5
        Assert.Equal(5, Calc("""
            void M() {
                if (a && b) {
                    if (c && d) { }
                }
            }
            """));
    }

    [Fact]
    public void ElseIf_SameOperator_CountedIndependently()
    {
        // if: +1, &&: +1, else if: +1, &&: +1 = 4
        Assert.Equal(4, Calc("""
            void M() {
                if (a && b) { }
                else if (c && d) { }
            }
            """));
    }

    // --- SonarSource spec compliance tests ---

    [Fact]
    public void WhileLoop_ReturnsOne()
    {
        Assert.Equal(1, Calc("void M() { while (true) { } }"));
    }

    [Fact]
    public void DoWhileLoop_ReturnsOne()
    {
        Assert.Equal(1, Calc("void M() { do { } while (true); }"));
    }

    [Fact]
    public void ForeachLoop_ReturnsOne()
    {
        Assert.Equal(1, Calc("void M() { foreach (var x in new int[0]) { } }"));
    }

    [Fact]
    public void CatchClause_ReturnsOne()
    {
        Assert.Equal(1, Calc("void M() { try { } catch { } }"));
    }

    [Fact]
    public void TryCatchFinally_OnlyCatchCounts()
    {
        // catch: +1, finally: 0
        Assert.Equal(1, Calc("void M() { try { } catch { } finally { } }"));
    }

    [Fact]
    public void ConditionalTernary_ReturnsOne()
    {
        Assert.Equal(1, Calc("int M() { return true ? 1 : 0; }"));
    }

    [Fact]
    public void SwitchExpression_ReturnsOne()
    {
        Assert.Equal(1, Calc("""
            int M() {
                return 1 switch { 0 => 0, _ => 1 };
            }
            """));
    }

    [Fact]
    public void DeepNesting_WhileForIf_ReturnsSix()
    {
        // while: +1(n=0), for: +1+1(n=1)=2, if: +1+2(n=2)=3 → total=6
        Assert.Equal(6, Calc("""
            void M() {
                while (true) {
                    for (int i = 0; i < 10; i++) {
                        if (true) { }
                    }
                }
            }
            """));
    }

    [Fact]
    public void MixedLogicalOperators_ThreeChanges()
    {
        // if: +1, &&: +1, ||: +1, &&: +1 = 4
        Assert.Equal(4, Calc("""
            void M() {
                if (a && b || c && d) { }
            }
            """));
    }

    [Fact]
    public void LambdaWithLoop_CountsNesting()
    {
        // lambda: nesting+1, for inside: +1+1(nesting=1) = 2
        Assert.Equal(2, Calc("""
            void M() {
                System.Action a = () => {
                    for (int i = 0; i < 10; i++) { }
                };
            }
            """));
    }

    // --- C# 9+ pattern combinator tests ---

    [Fact]
    public void OrPattern_Single_ReturnsOne()
    {
        // if: +1, or: +1 = 2
        Assert.Equal(2, Calc("""
            void M() {
                object x = 1;
                if (x is int or string) { }
            }
            """));
    }

    [Fact]
    public void OrPattern_Consecutive_SameKind_CountedOnce()
    {
        // if: +1, or (sequence): +1 = 2
        Assert.Equal(2, Calc("""
            void M() {
                object x = 1;
                if (x is int or string or double) { }
            }
            """));
    }

    [Fact]
    public void AndPattern_Single_ReturnsOne()
    {
        // if: +1, and: +1 = 2
        Assert.Equal(2, Calc("""
            void M() {
                object x = 1;
                if (x is > 0 and < 100) { }
            }
            """));
    }

    [Fact]
    public void OrAndPattern_Mixed_CountsBothChanges()
    {
        // if: +1, or: +1, and: +1 = 3
        Assert.Equal(3, Calc("""
            void M() {
                object x = 1;
                if (x is int or (> 0 and < 100)) { }
            }
            """));
    }

    [Fact]
    public void OrPattern_WithLogicalOr_SharedTracking()
    {
        // if: +1, ||: +1, or (same as ||, no change): +0 = 2
        Assert.Equal(2, Calc("""
            void M() {
                object x = 1;
                bool y = false;
                if (y || x is int or string) { }
            }
            """));
    }

    [Fact]
    public void AndPattern_WithLogicalAnd_SharedTracking()
    {
        // if: +1, &&: +1, and (same as &&, no change): +0 = 2
        Assert.Equal(2, Calc("""
            void M() {
                object x = 1;
                bool y = true;
                if (y && x is > 0 and < 100) { }
            }
            """));
    }

    [Fact]
    public void OrPattern_FollowedByLogicalAnd_KindChange()
    {
        // if: +1, or (mapped to ||): +1, && (kind change): +1 = 3
        Assert.Equal(3, Calc("""
            void M() {
                object x = 1;
                bool y = true;
                if (x is int or string && y) { }
            }
            """));
    }

    // --- Direct recursion tests ---

    [Fact]
    public void DirectRecursion_AddsOne()
    {
        // if: +1, recursion: +1 = 2
        Assert.Equal(2, Calc("""
            int M(int n) {
                if (n <= 0) return 0;
                return M(n - 1);
            }
            """));
    }

    [Fact]
    public void DirectRecursion_MultipleCalls_StillOne()
    {
        // if: +1, recursion: +1 (once regardless of call count) = 2
        Assert.Equal(2, Calc("""
            int M(int n) {
                if (n <= 1) return n;
                return M(n - 1) + M(n - 2);
            }
            """));
    }

    [Fact]
    public void NoRecursion_NoIncrement()
    {
        // if: +1, no recursion = 1
        Assert.Equal(1, Calc("""
            int M(int n) {
                if (n <= 0) return 0;
                return n;
            }
            """));
    }

    [Fact]
    public void DirectRecursion_WithNesting()
    {
        // if: +1, nested if: +2, recursion: +1 = 4
        Assert.Equal(4, Calc("""
            int M(int n) {
                if (n > 0) {
                    if (n % 2 == 0) return M(n - 1);
                }
                return 0;
            }
            """));
    }

    // --- Static local function tests ---

    [Fact]
    public void StaticLocalFunction_IndependentFromParent()
    {
        // Parent: if: +1 = 1
        // static local Inner is independent, not added to parent
        var code = """
            void M() {
                if (true) { }
                static void Inner() {
                    if (true) { }
                }
            }
            """;
        Assert.Equal(1, Calc(code));
    }

    [Fact]
    public void NonStaticLocalFunction_IncludedInParent()
    {
        // if: +1, non-static local func inner if: +1 (nesting not increased) = 2
        var code = """
            void M() {
                if (true) { }
                void Inner() {
                    if (true) { }
                }
            }
            """;
        Assert.Equal(2, Calc(code));
    }

    [Fact]
    public void StaticLocalFunction_OwnNesting()
    {
        // Parent: if: +1 = 1
        // static local Inner: independent (not counted in parent)
        var code = """
            void M() {
                if (true) {
                    static void Inner() {
                        if (true) {
                            if (true) { }
                        }
                    }
                }
            }
            """;
        Assert.Equal(1, Calc(code));
    }
}
