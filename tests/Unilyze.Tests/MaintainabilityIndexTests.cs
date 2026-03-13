namespace Unilyze.Tests;

public class MaintainabilityIndexTests
{
    static HalsteadMetrics CalcHalstead(string methodCode, string name = "M")
    {
        var code = $"class C {{ {methodCode} }}";
        var body = RoslynTestHelper.GetMethodBody(code, name);
        return HalsteadCalculator.Calculate(body);
    }

    // --- Halstead tests ---

    [Fact]
    public void Halstead_EmptyMethod_ZeroVolume()
    {
        var result = CalcHalstead("void M() { }");
        Assert.Equal(0, result.Volume);
        Assert.Equal(0, result.TotalOperators);
        Assert.Equal(0, result.TotalOperands);
    }

    [Fact]
    public void Halstead_NullBody_ZeroVolume()
    {
        var result = HalsteadCalculator.Calculate(null);
        Assert.Equal(0, result.Volume);
    }

    [Fact]
    public void Halstead_SimpleAssignment()
    {
        // x = 1 → operators: {=}, operands: {x, 1}
        var result = CalcHalstead("void M() { var x = 1; }");
        Assert.True(result.TotalOperators > 0);
        Assert.True(result.TotalOperands > 0);
        Assert.True(result.Volume > 0);
    }

    [Fact]
    public void Halstead_OperatorCounting()
    {
        // a + b * c → operators include +, *, =
        var result = CalcHalstead("int M() { var r = a + b * c; return r; }");
        Assert.True(result.UniqueOperators >= 3); // =, +, *, return
        Assert.True(result.TotalOperators >= 3);
    }

    [Fact]
    public void Halstead_LiteralsCounted()
    {
        // Multiple literals of different types
        var result = CalcHalstead("""void M() { var a = 1; var b = "hello"; var c = true; }""");
        Assert.True(result.UniqueOperands >= 3); // 1, "hello", true (plus a, b, c)
        Assert.True(result.TotalOperands >= 6);
    }

    [Fact]
    public void Halstead_KeywordOperators()
    {
        var result = CalcHalstead("""
            int M() {
                if (true) return 1;
                for (var i = 0; i < 10; i++) { }
                return 0;
            }
            """);
        // if, for, return (x2) are keyword operators
        Assert.True(result.TotalOperators >= 4);
    }

    // --- MI formula tests ---

    [Fact]
    public void MI_EmptyMethod_ReturnsHundred()
    {
        // HV = 0 → MI = 100
        var mi = HalsteadCalculator.ComputeMaintainabilityIndex(0, 1, 1);
        Assert.Equal(100.0, mi);
    }

    [Fact]
    public void MI_Formula_KnownValues()
    {
        // HV = 100, CC = 10, LoC = 50
        // MI = max(0, (171 - 5.2*ln(100) - 0.23*10 - 16.2*ln(50)) * 100/171)
        // ln(100) ≈ 4.605, ln(50) ≈ 3.912
        // = max(0, (171 - 23.946 - 2.3 - 63.374) * 100/171)
        // = max(0, 81.38 * 100/171)
        // ≈ 47.59
        var mi = HalsteadCalculator.ComputeMaintainabilityIndex(100, 10, 50);
        Assert.InRange(mi, 45.0, 50.0);
    }

    [Fact]
    public void MI_NeverNegative()
    {
        // Extremely complex: HV = 1_000_000, CC = 500, LoC = 10_000
        var mi = HalsteadCalculator.ComputeMaintainabilityIndex(1_000_000, 500, 10_000);
        Assert.True(mi >= 0);
    }

    [Fact]
    public void MI_ComplexMethod_Low()
    {
        // A method with many branches and tokens
        var result = CalcHalstead("""
            int M() {
                var sum = 0;
                for (var i = 0; i < 100; i++) {
                    if (i % 2 == 0) {
                        sum += i;
                    } else if (i % 3 == 0) {
                        sum -= i;
                    } else if (i % 5 == 0) {
                        sum *= 2;
                    } else if (i % 7 == 0) {
                        sum /= 2;
                    } else {
                        sum += 1;
                    }
                    if (sum > 1000) {
                        sum = 0;
                    }
                    while (sum < 0) {
                        sum += 10;
                    }
                }
                return sum;
            }
            """);
        Assert.True(result.Volume > 0);

        var body = RoslynTestHelper.GetMethodBody(
            $"class C {{ int M() {{ var sum = 0; for (var i = 0; i < 100; i++) {{ if (i % 2 == 0) {{ sum += i; }} else if (i % 3 == 0) {{ sum -= i; }} else if (i % 5 == 0) {{ sum *= 2; }} else if (i % 7 == 0) {{ sum /= 2; }} else {{ sum += 1; }} if (sum > 1000) {{ sum = 0; }} while (sum < 0) {{ sum += 10; }} }} return sum; }} }}",
            "M");
        var cycCC = CyclomaticComplexity.Calculate(body);
        var mi = HalsteadCalculator.ComputeMaintainabilityIndex(result.Volume, cycCC, 20);
        Assert.True(mi < 80);
    }

    // --- Integration tests ---

    [Fact]
    public void MI_MethodMetrics_Integration()
    {
        var code = """
            namespace Test;
            class MyClass {
                int Add(int a, int b) { return a + b; }
            }
            """;
        var types = RoslynTestHelper.ExtractTypesFromCode(code);
        var metrics = CodeHealthCalculator.ComputeTypeMetrics(types);

        Assert.Single(metrics);
        var type = metrics[0];
        Assert.Single(type.Methods);
        var method = type.Methods[0];
        Assert.NotNull(method.MaintainabilityIndex);
        Assert.True(method.MaintainabilityIndex > 0);
    }

    [Fact]
    public void MI_TypeMetrics_Aggregation()
    {
        var code = """
            namespace Test;
            class MyClass {
                int Add(int a, int b) { return a + b; }
                int Sub(int a, int b) { return a - b; }
            }
            """;
        var types = RoslynTestHelper.ExtractTypesFromCode(code);
        var metrics = CodeHealthCalculator.ComputeTypeMetrics(types);

        Assert.Single(metrics);
        var type = metrics[0];
        Assert.NotNull(type.AverageMaintainabilityIndex);
        Assert.NotNull(type.MinMaintainabilityIndex);
        Assert.True(type.AverageMaintainabilityIndex > 0);
        Assert.True(type.MinMaintainabilityIndex <= type.AverageMaintainabilityIndex);
    }

    // --- CodeSmell tests ---

    [Fact]
    public void LowMaintainability_SmellDetected()
    {
        var methods = new List<MethodMetrics>
        {
            new("BadMethod", 5, 3, 2, 2, 50, MaintainabilityIndex: 15.0)
        };
        var metrics = new TypeMetrics(
            "TestClass", "TestNs", "TestAssembly",
            200, 1, 2, 5.0, 5, 3.0, 3, 0, 7.0, methods);
        var typeInfo = new TypeNodeInfo(
            "TestClass", "TestNs", "class",
            ["public"], null, [], [], [], [], [], null,
            "TestAssembly", "test.cs", false, 200);
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        var smell = Assert.Single(smells, s => s.Kind == CodeSmellKind.LowMaintainability);
        Assert.Equal(SmellSeverity.Warning, smell.Severity);
    }

    [Fact]
    public void CriticalMaintainability_SmellDetected()
    {
        var methods = new List<MethodMetrics>
        {
            new("TerribleMethod", 5, 3, 2, 2, 50, MaintainabilityIndex: 5.0)
        };
        var metrics = new TypeMetrics(
            "TestClass", "TestNs", "TestAssembly",
            200, 1, 2, 5.0, 5, 3.0, 3, 0, 7.0, methods);
        var typeInfo = new TypeNodeInfo(
            "TestClass", "TestNs", "class",
            ["public"], null, [], [], [], [], [], null,
            "TestAssembly", "test.cs", false, 200);
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        var smell = Assert.Single(smells, s => s.Kind == CodeSmellKind.LowMaintainability);
        Assert.Equal(SmellSeverity.Critical, smell.Severity);
    }
}
