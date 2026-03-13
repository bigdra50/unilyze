using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests;

public class DitAndCouplingTests
{
    // --- DIT tests ---

    static int CalcDit(string code, string typeName = "C")
    {
        var typeDecl = RoslynTestHelper.GetType(code, typeName);
        return DitCalculator.Calculate(typeDecl, model: null);
    }

    static int CalcDitSemantic(string code, string typeName = "C")
    {
        var model = RoslynTestHelper.CreateSemanticModel(code);
        var typeDecl = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == typeName);
        return DitCalculator.Calculate(typeDecl, model);
    }

    [Fact]
    public void DIT_NoBase_Zero()
    {
        Assert.Equal(0, CalcDit("class C { }"));
    }

    [Fact]
    public void DIT_SingleInheritance_Syntactic()
    {
        var code = """
            class Base { }
            class C : Base { }
            """;
        Assert.Equal(1, CalcDit(code));
    }

    [Fact]
    public void DIT_Interface_Zero()
    {
        Assert.Equal(0, CalcDit("interface C { }"));
    }

    [Fact]
    public void DIT_DeepChain_Semantic()
    {
        var code = """
            class A { }
            class B : A { }
            class D : B { }
            class C : D { }
            """;
        Assert.Equal(3, CalcDitSemantic(code));
    }

    [Fact]
    public void DIT_Struct_Zero()
    {
        Assert.Equal(0, CalcDit("struct C { }"));
    }

    [Fact]
    public void DIT_InterfaceOnly_Syntactic_Zero()
    {
        var code = """
            interface IFoo { }
            class C : IFoo { }
            """;
        Assert.Equal(0, CalcDit(code));
    }

    [Fact]
    public void DIT_Semantic_SingleInheritance()
    {
        var code = """
            class Base { }
            class C : Base { }
            """;
        Assert.Equal(1, CalcDitSemantic(code));
    }

    [Fact]
    public void DIT_Semantic_NoBase_Zero()
    {
        Assert.Equal(0, CalcDitSemantic("class C { }"));
    }

    // --- Ca/Ce/Instability tests ---

    static IReadOnlyDictionary<string, CouplingInfo> CalcCoupling(
        IReadOnlyList<TypeDependency> deps,
        params string[] typeNames)
    {
        var types = typeNames.Select(n =>
            new TypeNodeInfo(n, "", "class", [], null, [], [], [], [], [], null, "Asm", "test.cs", false, 10))
            .ToList();
        return CouplingMetricsCalculator.Calculate(deps, types);
    }

    [Fact]
    public void Ca_NoIncoming_Zero()
    {
        var deps = new List<TypeDependency>();
        var result = CalcCoupling(deps, "A", "B");
        Assert.Equal(0, result["A"].AfferentCoupling);
    }

    [Fact]
    public void Ca_MultipleDependents()
    {
        var deps = new List<TypeDependency>
        {
            new("A", "C", DependencyKind.FieldType),
            new("B", "C", DependencyKind.FieldType),
        };
        var result = CalcCoupling(deps, "A", "B", "C");
        Assert.Equal(2, result["C"].AfferentCoupling);
    }

    [Fact]
    public void Ce_NoDeps_Zero()
    {
        var deps = new List<TypeDependency>();
        var result = CalcCoupling(deps, "A");
        Assert.Equal(0, result["A"].EfferentCoupling);
    }

    [Fact]
    public void Ce_MultipleDeps()
    {
        var deps = new List<TypeDependency>
        {
            new("A", "B", DependencyKind.FieldType),
            new("A", "C", DependencyKind.MethodParam),
        };
        var result = CalcCoupling(deps, "A", "B", "C");
        Assert.Equal(2, result["A"].EfferentCoupling);
    }

    [Fact]
    public void Instability_AllEfferent_One()
    {
        var deps = new List<TypeDependency>
        {
            new("A", "B", DependencyKind.FieldType),
        };
        var result = CalcCoupling(deps, "A", "B");
        // A: Ca=0, Ce=1 → I=1.0
        Assert.Equal(1.0, result["A"].Instability);
    }

    [Fact]
    public void Instability_AllAfferent_Zero()
    {
        var deps = new List<TypeDependency>
        {
            new("A", "B", DependencyKind.FieldType),
        };
        var result = CalcCoupling(deps, "A", "B");
        // B: Ca=1, Ce=0 → I=0.0
        Assert.Equal(0.0, result["B"].Instability);
    }

    [Fact]
    public void Instability_NoDeps_Null()
    {
        var deps = new List<TypeDependency>();
        var result = CalcCoupling(deps, "A");
        Assert.Null(result["A"].Instability);
    }

    [Fact]
    public void Instability_Balanced()
    {
        var deps = new List<TypeDependency>
        {
            new("A", "B", DependencyKind.FieldType),
            new("C", "A", DependencyKind.FieldType),
        };
        var result = CalcCoupling(deps, "A", "B", "C");
        // A: Ca=1, Ce=1 → I=0.5
        Assert.Equal(0.5, result["A"].Instability);
    }

    // --- DeepInheritance smell tests ---

    [Fact]
    public void DeepInheritance_SmellDetected()
    {
        var typeInfo = new TypeNodeInfo(
            "C", "TestNs", "class",
            ["public"], null, [], [], [], [], [], null,
            "TestAssembly", "/test.cs", false, 100);
        var metrics = new TypeMetrics(
            "C", "TestNs", "TestAssembly",
            100, 5, 2, 3.0, 5, 3.0, 5, 0, 8.0, []);

        var smells = CodeSmellDetector.Detect(metrics, typeInfo, null, dit: 6);

        var smell = Assert.Single(smells, s => s.Kind == CodeSmellKind.DeepInheritance);
        Assert.Equal(SmellSeverity.Warning, smell.Severity);
    }

    [Fact]
    public void DeepInheritance_BelowThreshold_NoSmell()
    {
        var typeInfo = new TypeNodeInfo(
            "C", "TestNs", "class",
            ["public"], null, [], [], [], [], [], null,
            "TestAssembly", "/test.cs", false, 100);
        var metrics = new TypeMetrics(
            "C", "TestNs", "TestAssembly",
            100, 5, 2, 3.0, 5, 3.0, 5, 0, 8.0, []);

        var smells = CodeSmellDetector.Detect(metrics, typeInfo, null, dit: 5);

        Assert.DoesNotContain(smells, s => s.Kind == CodeSmellKind.DeepInheritance);
    }

    // --- TypeMetrics integration ---

    [Fact]
    public void CouplingMetrics_TypeMetrics_Integration()
    {
        var metrics = new TypeMetrics(
            "A", "TestNs", "TestAssembly",
            100, 5, 2, 3.0, 5, 3.0, 5, 0, 8.0, [],
            AfferentCoupling: 3,
            EfferentCoupling: 2,
            Instability: 0.4);

        Assert.Equal(3, metrics.AfferentCoupling);
        Assert.Equal(2, metrics.EfferentCoupling);
        Assert.Equal(0.4, metrics.Instability);
    }
}
