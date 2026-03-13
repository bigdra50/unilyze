using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests;

public class CboCalculatorTests
{
    static int Calc(string code, string typeName = "C")
    {
        var typeDecl = RoslynTestHelper.GetType(code, typeName);
        return CboCalculator.Calculate(typeDecl, model: null);
    }

    static int CalcSemantic(string code, string typeName = "C")
    {
        var model = RoslynTestHelper.CreateSemanticModel(code);
        var typeDecl = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == typeName);
        return CboCalculator.Calculate(typeDecl, model);
    }

    // --- Syntactic tests ---

    [Fact]
    public void EmptyClass_Zero()
    {
        Assert.Equal(0, Calc("class C { }"));
    }

    [Fact]
    public void SingleFieldType()
    {
        var code = """
            class A { }
            class C { A _field; }
            """;
        Assert.Equal(1, Calc(code));
    }

    [Fact]
    public void MultipleDependencies()
    {
        var code = """
            class A { }
            class B { }
            class D { }
            class C {
                A _a;
                B Prop { get; set; }
                void Foo(D d) { }
            }
            """;
        Assert.Equal(3, Calc(code));
    }

    [Fact]
    public void DuplicateType_CountedOnce()
    {
        var code = """
            class A { }
            class C {
                A _a1;
                A _a2;
                void Foo(A a) { }
            }
            """;
        Assert.Equal(1, Calc(code));
    }

    [Fact]
    public void SelfReference_Excluded()
    {
        var code = """
            class C {
                C _self;
                C Next { get; set; }
            }
            """;
        Assert.Equal(0, Calc(code));
    }

    [Fact]
    public void PrimitiveTypes_Excluded()
    {
        var code = """
            class C {
                int _i;
                string _s;
                bool _b;
                double _d;
                void Foo(float f, decimal d, long l) { }
            }
            """;
        Assert.Equal(0, Calc(code));
    }

    [Fact]
    public void GenericTypeArguments()
    {
        var code = """
            class A { }
            class MyList<T> { }
            class C {
                MyList<A> _list;
            }
            """;
        // MyList and A are both counted
        Assert.Equal(2, Calc(code));
    }

    [Fact]
    public void BaseTypeAndInterfaces()
    {
        var code = """
            class Base { }
            interface IFoo { }
            class C : Base, IFoo { }
            """;
        Assert.Equal(2, Calc(code));
    }

    [Fact]
    public void MethodBodyTypes()
    {
        var code = """
            class A { }
            class B { }
            class C {
                void Foo() {
                    A a = new A();
                    B b = (B)null;
                }
            }
            """;
        Assert.Equal(2, Calc(code));
    }

    // --- Semantic tests ---

    [Fact]
    public void Semantic_EmptyClass_Zero()
    {
        Assert.Equal(0, CalcSemantic("class C { }"));
    }

    [Fact]
    public void Semantic_MultipleDependencies()
    {
        var code = """
            class A { }
            class B { }
            class C {
                A _a;
                B _b;
            }
            """;
        Assert.Equal(2, CalcSemantic(code));
    }

    [Fact]
    public void Semantic_PrimitiveTypes_Excluded()
    {
        var code = """
            class C {
                int _i;
                string _s;
                bool _b;
            }
            """;
        Assert.Equal(0, CalcSemantic(code));
    }

    [Fact]
    public void Semantic_SelfReference_Excluded()
    {
        var code = """
            class C {
                C _self;
            }
            """;
        Assert.Equal(0, CalcSemantic(code));
    }

    // --- CodeSmell integration ---

    [Fact]
    public void HighCoupling_SmellDetected()
    {
        var typeInfo = new TypeNodeInfo(
            "C", "TestNs", "class",
            ["public"], null, [], [], [], [], [], null,
            "TestAssembly", "/test.cs", false, 100);
        var metrics = new TypeMetrics(
            "C", "TestNs", "TestAssembly",
            100, 5, 2, 3.0, 5, 3.0, 5, 0, 8.0, []);

        var smells = CodeSmellDetector.Detect(metrics, typeInfo, null, cbo: 14);

        var coupling = Assert.Single(smells, s => s.Kind == CodeSmellKind.HighCoupling);
        Assert.Equal(SmellSeverity.Warning, coupling.Severity);
    }

    [Fact]
    public void CriticalCoupling_SmellDetected()
    {
        var typeInfo = new TypeNodeInfo(
            "C", "TestNs", "class",
            ["public"], null, [], [], [], [], [], null,
            "TestAssembly", "/test.cs", false, 100);
        var metrics = new TypeMetrics(
            "C", "TestNs", "TestAssembly",
            100, 5, 2, 3.0, 5, 3.0, 5, 0, 8.0, []);

        var smells = CodeSmellDetector.Detect(metrics, typeInfo, null, cbo: 25);

        var coupling = Assert.Single(smells, s => s.Kind == CodeSmellKind.HighCoupling);
        Assert.Equal(SmellSeverity.Critical, coupling.Severity);
    }

    [Fact]
    public void BelowThreshold_NoSmell()
    {
        var typeInfo = new TypeNodeInfo(
            "C", "TestNs", "class",
            ["public"], null, [], [], [], [], [], null,
            "TestAssembly", "/test.cs", false, 100);
        var metrics = new TypeMetrics(
            "C", "TestNs", "TestAssembly",
            100, 5, 2, 3.0, 5, 3.0, 5, 0, 8.0, []);

        var smells = CodeSmellDetector.Detect(metrics, typeInfo, null, cbo: 13);

        Assert.DoesNotContain(smells, s => s.Kind == CodeSmellKind.HighCoupling);
    }
}
