using Unilyze.Tests.Helpers;

namespace Unilyze.Tests;

public class CognitiveComplexityWhitepaperTests
{
    static int CalcFullClass(string classCode, string name = "M")
    {
        var body = RoslynTestHelper.GetMethodBody(classCode, name);
        return CognitiveComplexity.Calculate(body);
    }

    static async Task<int> SonarCalc(string code, string name = "M")
    {
        var scores = await SonarCogCCHelper.GetCognitiveComplexities(code);
        Assert.True(scores.ContainsKey(name),
            $"SonarAnalyzer should report CogCC for method '{name}'");
        return scores[name];
    }

    // --- SonarAnalyzer integration smoke test ---

    [Fact]
    public async Task SonarAnalyzer_ReturnsValues()
    {
        const string code = """
            class C {
                public void M() {
                    if (true) {
                        if (false) { }
                    }
                }
            }
            """;
        var sonar = await SonarCalc(code);
        Assert.Equal(3, sonar);
    }

    // --- Whitepaper / spec compliance tests ---

    [Fact]
    public async Task Appendix_SumOfPrimes()
    {
        // for: +1, for: +2(nesting=1), if: +3(nesting=2) = 6
        const string code = """
            class C {
                public int M(int max) {
                    int total = 0;
                    for (int i = 1; i <= max; i++) {
                        for (int j = 2; j < i; j++) {
                            if (i % j == 0) {
                                break;
                            }
                        }
                        total += i;
                    }
                    return total;
                }
            }
            """;
        const int expected = 6;
        Assert.Equal(expected, CalcFullClass(code));
        Assert.Equal(expected, await SonarCalc(code));
    }

    [Fact]
    public async Task Appendix_GetWords()
    {
        // if: +1, else if: +1, else if: +1, else: +1 = 4
        const string code = """
            class C {
                public string M(int number) {
                    if (number == 1) {
                        return "one";
                    } else if (number == 2) {
                        return "two";
                    } else if (number == 3) {
                        return "a few";
                    } else {
                        return "lots";
                    }
                }
            }
            """;
        const int expected = 4;
        Assert.Equal(expected, CalcFullClass(code));
        Assert.Equal(expected, await SonarCalc(code));
    }

    [Fact]
    public async Task MixedOperators_AndOrAnd()
    {
        // &&: +1, ||: +1, &&: +1 = 3
        const string code = """
            class C {
                public bool M(bool a, bool b, bool c, bool d) {
                    return a && b || c && d;
                }
            }
            """;
        const int expected = 3;
        Assert.Equal(expected, CalcFullClass(code));
        Assert.Equal(expected, await SonarCalc(code));
    }

    [Fact]
    public async Task DeepNesting_ThreeLevel()
    {
        // if: +1(n=0), if: +2(n=1), if: +3(n=2) = 6
        const string code = """
            class C {
                public void M(bool x, bool y, bool z) {
                    if (x) {
                        if (y) {
                            if (z) {
                            }
                        }
                    }
                }
            }
            """;
        const int expected = 6;
        Assert.Equal(expected, CalcFullClass(code));
        Assert.Equal(expected, await SonarCalc(code));
    }

    [Fact]
    public async Task ElseIfChain_FourBranches()
    {
        // if: +1, else if: +1 x3, else: +1 = 5
        const string code = """
            class C {
                public int M(int x) {
                    if (x == 1) {
                        return 1;
                    } else if (x == 2) {
                        return 2;
                    } else if (x == 3) {
                        return 3;
                    } else if (x == 4) {
                        return 4;
                    } else {
                        return 0;
                    }
                }
            }
            """;
        const int expected = 5;
        Assert.Equal(expected, CalcFullClass(code));
        Assert.Equal(expected, await SonarCalc(code));
    }

    [Fact]
    public async Task LambdaWithCondition()
    {
        // lambda: nesting+1, if inside lambda: +1+1(nesting) = 2
        const string code = """
            using System;
            class C {
                public void M() {
                    Action a = () => {
                        if (true) { }
                    };
                }
            }
            """;
        const int expected = 2;
        Assert.Equal(expected, CalcFullClass(code));
        Assert.Equal(expected, await SonarCalc(code));
    }

    [Fact]
    public async Task BreakContinue_NoIncrement()
    {
        // for: +1, if: +2(nesting=1), if: +2(nesting=1) = 5
        const string code = """
            class C {
                public void M() {
                    for (int i = 0; i < 10; i++) {
                        if (i == 5) break;
                        if (i == 3) continue;
                    }
                }
            }
            """;
        const int expected = 5;
        Assert.Equal(expected, CalcFullClass(code));
        Assert.Equal(expected, await SonarCalc(code));
    }

    [Fact]
    public async Task TernaryOperator_PlusNesting()
    {
        // if: +1, ternary: +1+1(nesting=1) = 3
        const string code = """
            class C {
                public int M(bool a, bool b) {
                    if (a) {
                        return b ? 1 : 0;
                    }
                    return 0;
                }
            }
            """;
        const int expected = 3;
        Assert.Equal(expected, CalcFullClass(code));
        Assert.Equal(expected, await SonarCalc(code));
    }
}
