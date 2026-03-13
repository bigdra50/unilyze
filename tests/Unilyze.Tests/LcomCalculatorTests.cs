namespace Unilyze.Tests;

public class LcomCalculatorTests
{
    static double? Calc(string classCode)
    {
        var typeDecl = RoslynTestHelper.GetType(classCode, "C");
        return LcomCalculator.Calculate(typeDecl, model: null);
    }

    static double? CalcWithSemantic(string classCode)
    {
        var model = RoslynTestHelper.CreateSemanticModel(classCode);
        var typeDecl = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == "C");
        return LcomCalculator.Calculate(typeDecl, model);
    }

    [Fact]
    public void NoFields_ReturnsNull()
    {
        Assert.Null(Calc("""
            class C {
                void M1() { }
                void M2() { }
            }
            """));
    }

    [Fact]
    public void ZeroOrOneMethods_ReturnsNull()
    {
        Assert.Null(Calc("""
            class C {
                int _x;
                void M() { var a = _x; }
            }
            """));
    }

    [Fact]
    public void FullyCohesive_ReturnsZero()
    {
        // M=2, F=1 (_x), both methods access _x
        // sum(mA) = 2, avg = 2/1 = 2
        // LCOM = (2 - 2) / (1 - 2) = 0 / -1 = 0
        var result = Calc("""
            class C {
                int _x;
                void M1() { var a = _x; }
                void M2() { _x = 1; }
            }
            """);
        Assert.NotNull(result);
        Assert.Equal(0.0, result!.Value);
    }

    [Fact]
    public void FullySeparated_ReturnsOne()
    {
        // M=2, F=2, M1 accesses _x only, M2 accesses _y only
        // sum(mA) = 1 + 1 = 2, avg = 2/2 = 1
        // LCOM = (1 - 2) / (1 - 2) = -1 / -1 = 1.0
        var result = Calc("""
            class C {
                int _x;
                int _y;
                void M1() { var a = _x; }
                void M2() { var a = _y; }
            }
            """);
        Assert.NotNull(result);
        Assert.Equal(1.0, result!.Value);
    }

    [Fact]
    public void PartialCohesion_ReturnsExpected()
    {
        // M=3, F=3 (_a, _b, _c)
        // M1 accesses _a, _b → mA(_a)=1, mA(_b)=1
        // M2 accesses _b, _c → mA(_b)+=1 → mA(_b)=2, mA(_c)=1
        // M3 accesses _a → mA(_a)+=1 → mA(_a)=2
        // sum = 2 + 2 + 1 = 5, avg = 5/3
        // LCOM = (5/3 - 3) / (1 - 3) = (5/3 - 9/3) / -2 = (-4/3) / -2 = 4/6 = 0.67
        var result = Calc("""
            class C {
                int _a;
                int _b;
                int _c;
                void M1() { var x = _a + _b; }
                void M2() { var x = _b + _c; }
                void M3() { var x = _a; }
            }
            """);
        Assert.NotNull(result);
        Assert.Equal(0.67, result!.Value);
    }

    [Fact]
    public void AutoProperty_ExcludedFromFields()
    {
        // After fix: auto-property should NOT be counted as a field
        // Only _x is a field. M=2, F=1
        // M1 accesses _x, M2 accesses _x
        // sum(mA) = 2, avg = 2/1 = 2
        // LCOM = (2 - 2) / (1 - 2) = 0
        var result = Calc("""
            class C {
                int _x;
                int Prop { get; set; }
                void M1() { var a = _x; }
                void M2() { _x = 1; }
            }
            """);
        Assert.NotNull(result);
        Assert.Equal(0.0, result!.Value);
    }

    [Fact]
    public void Constructor_IncludedInMethods()
    {
        // After fix: constructor counts as a method
        // M=2 (M1 + ctor), F=1 (_x)
        // ctor accesses _x, M1 accesses _x
        // sum(mA) = 2, avg = 2/1 = 2
        // LCOM = (2 - 2) / (1 - 2) = 0
        var result = Calc("""
            class C {
                int _x;
                C() { _x = 0; }
                void M1() { var a = _x; }
            }
            """);
        Assert.NotNull(result);
        Assert.Equal(0.0, result!.Value);
    }

    [Fact]
    public void Constructor_NotAccessingField_AffectsLcom()
    {
        // M=2 (ctor + M1), F=1 (_x)
        // ctor doesn't access _x (mA=0 for ctor), M1 accesses _x
        // sum(mA) = 1, avg = 1/1 = 1
        // LCOM = (1 - 2) / (1 - 2) = -1 / -1 = 1.0
        var result = Calc("""
            class C {
                int _x;
                C() { }
                void M1() { var a = _x; }
            }
            """);
        Assert.NotNull(result);
        Assert.Equal(1.0, result!.Value);
    }

    [Fact]
    public void StaticConstructor_Excluded()
    {
        // Static constructor should NOT be counted
        // M=1 (M1 only) → returns null (<=1 methods)
        Assert.Null(Calc("""
            class C {
                int _x;
                static C() { }
                void M1() { var a = _x; }
            }
            """));
    }
}
