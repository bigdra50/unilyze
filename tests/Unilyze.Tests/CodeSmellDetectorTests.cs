namespace Unilyze.Tests;

public class CodeSmellDetectorTests
{
    static TypeMetrics MakeTypeMetrics(
        int lineCount = 100, int methodCount = 5, double avgCogCC = 3.0,
        int maxCogCC = 5, IReadOnlyList<MethodMetrics>? methods = null)
    {
        methods ??= [];
        return new TypeMetrics(
            "TestClass", "TestNs", "TestAssembly",
            lineCount, methodCount, 1,
            avgCogCC, maxCogCC,
            3.0, 5,
            0, 8.0,
            methods);
    }

    static TypeNodeInfo MakeTypeInfo(int lineCount = 100)
    {
        return new TypeNodeInfo(
            "TestClass", "TestNs", "class",
            ["public"], null, [], [], [], [], [], null,
            "TestAssembly", "test.cs", false, lineCount);
    }

    [Fact]
    public void NoSmells_WhenBelowThresholds()
    {
        var metrics = MakeTypeMetrics();
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: 0.3);
        Assert.Empty(smells);
    }

    [Fact]
    public void DetectsGodClass_ByLineCount()
    {
        var metrics = MakeTypeMetrics(lineCount: 600);
        var typeInfo = MakeTypeInfo(lineCount: 600);
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.Contains(smells, s => s.Kind == CodeSmellKind.GodClass);
    }

    [Fact]
    public void DetectsGodClass_ByMethodCount()
    {
        var metrics = MakeTypeMetrics(methodCount: 25);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.Contains(smells, s => s.Kind == CodeSmellKind.GodClass);
    }

    [Fact]
    public void DetectsLowCohesion_WhenLcomHigh()
    {
        var metrics = MakeTypeMetrics();
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: 0.9);
        Assert.Contains(smells, s => s.Kind == CodeSmellKind.LowCohesion);
    }

    [Fact]
    public void DetectsLongMethod()
    {
        var methods = new List<MethodMetrics>
        {
            new("LongMethod", 30, 10, 2, 3, 100)
        };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.Contains(smells, s => s.Kind == CodeSmellKind.LongMethod);
    }

    [Fact]
    public void DetectsDeepNesting()
    {
        var methods = new List<MethodMetrics>
        {
            new("DeepMethod", 5, 3, 5, 2, 20)
        };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.Contains(smells, s => s.Kind == CodeSmellKind.DeepNesting);
    }

    [Fact]
    public void DetectsExcessiveParameters()
    {
        var methods = new List<MethodMetrics>
        {
            new("ManyParams", 3, 2, 1, 7, 10)
        };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.Contains(smells, s => s.Kind == CodeSmellKind.ExcessiveParameters);
    }

    // --- Boundary value tests ---

    [Fact]
    public void GodClass_BelowBothThresholds_NoSmell()
    {
        var metrics = MakeTypeMetrics(lineCount: 499, methodCount: 19);
        var typeInfo = MakeTypeInfo(lineCount: 499);
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.DoesNotContain(smells, s => s.Kind == CodeSmellKind.GodClass);
    }

    [Fact]
    public void GodClass_AtLineThreshold_Warning()
    {
        var metrics = MakeTypeMetrics(lineCount: 500, methodCount: 5);
        var typeInfo = MakeTypeInfo(lineCount: 500);
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        var godClass = Assert.Single(smells, s => s.Kind == CodeSmellKind.GodClass);
        Assert.Equal(SmellSeverity.Warning, godClass.Severity);
    }

    [Fact]
    public void GodClass_AtMethodThreshold_Warning()
    {
        var metrics = MakeTypeMetrics(lineCount: 100, methodCount: 20);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        var godClass = Assert.Single(smells, s => s.Kind == CodeSmellKind.GodClass);
        Assert.Equal(SmellSeverity.Warning, godClass.Severity);
    }

    [Fact]
    public void GodClass_BothThresholds_Critical()
    {
        var metrics = MakeTypeMetrics(lineCount: 1000, methodCount: 20);
        var typeInfo = MakeTypeInfo(lineCount: 1000);
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        var godClass = Assert.Single(smells, s => s.Kind == CodeSmellKind.GodClass);
        Assert.Equal(SmellSeverity.Critical, godClass.Severity);
    }

    [Fact]
    public void LongMethod_AtLineThreshold_Detected()
    {
        var methods = new List<MethodMetrics>
        {
            new("Method", 3, 2, 1, 2, 80)
        };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.Contains(smells, s => s.Kind == CodeSmellKind.LongMethod);
    }

    [Fact]
    public void LongMethod_BelowThreshold_NoSmell()
    {
        var methods = new List<MethodMetrics>
        {
            new("Method", 3, 2, 1, 2, 79)
        };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.DoesNotContain(smells, s => s.Kind == CodeSmellKind.LongMethod);
    }

    [Fact]
    public void DeepNesting_AtThreshold_Warning()
    {
        var methods = new List<MethodMetrics>
        {
            new("Method", 3, 2, 4, 2, 20)
        };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        var deep = Assert.Single(smells, s => s.Kind == CodeSmellKind.DeepNesting);
        Assert.Equal(SmellSeverity.Warning, deep.Severity);
    }

    [Fact]
    public void DeepNesting_AtCriticalThreshold_Critical()
    {
        var methods = new List<MethodMetrics>
        {
            new("Method", 3, 2, 6, 2, 20)
        };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        var deep = Assert.Single(smells, s => s.Kind == CodeSmellKind.DeepNesting);
        Assert.Equal(SmellSeverity.Critical, deep.Severity);
    }

    [Fact]
    public void LowCohesion_BelowThreshold_NoSmell()
    {
        var metrics = MakeTypeMetrics();
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: 0.79);
        Assert.DoesNotContain(smells, s => s.Kind == CodeSmellKind.LowCohesion);
    }

    [Fact]
    public void LowCohesion_AtThreshold_Detected()
    {
        var metrics = MakeTypeMetrics();
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: 0.80);
        Assert.Contains(smells, s => s.Kind == CodeSmellKind.LowCohesion);
    }

    // --- Additional boundary value tests ---

    // ExcessiveParameters boundary: <= 5 no smell, > 5 smell
    [Fact]
    public void ExcessiveParameters_AtThreshold5_NoSmell()
    {
        var methods = new List<MethodMetrics> { new("M", 1, 1, 1, 5, 10) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.DoesNotContain(smells, s => s.Kind == CodeSmellKind.ExcessiveParameters);
    }

    [Fact]
    public void ExcessiveParameters_At6_Detected()
    {
        var methods = new List<MethodMetrics> { new("M", 1, 1, 1, 6, 10) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.Contains(smells, s => s.Kind == CodeSmellKind.ExcessiveParameters);
    }

    // HighComplexity CycCC boundary: < 15 no, >= 15 yes
    [Fact]
    public void HighComplexity_CycCC14_NoSmell()
    {
        var methods = new List<MethodMetrics> { new("M", 1, 14, 1, 1, 10) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.DoesNotContain(smells, s => s.Kind == CodeSmellKind.HighComplexity);
    }

    [Fact]
    public void HighComplexity_CycCC15_Detected()
    {
        var methods = new List<MethodMetrics> { new("M", 1, 15, 1, 1, 10) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.Contains(smells, s => s.Kind == CodeSmellKind.HighComplexity);
    }

    // HighComplexity CogCC boundary: 14 no, 15 yes (HighComplexity), 24 yes (HighComplexity)
    [Fact]
    public void HighComplexity_CogCC14_NoSmell()
    {
        var methods = new List<MethodMetrics> { new("M", 14, 1, 1, 1, 10) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.DoesNotContain(smells, s => s.Kind == CodeSmellKind.HighComplexity);
    }

    [Fact]
    public void HighComplexity_CogCC15_Detected()
    {
        var methods = new List<MethodMetrics> { new("M", 15, 1, 1, 1, 10) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.Contains(smells, s => s.Kind == CodeSmellKind.HighComplexity);
    }

    [Fact]
    public void HighComplexity_CogCC24_StillHighComplexity()
    {
        var methods = new List<MethodMetrics> { new("M", 24, 1, 1, 1, 10) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.Contains(smells, s => s.Kind == CodeSmellKind.HighComplexity);
    }

    // HighComplexity vs LongMethod exclusivity: CogCC=25 triggers LongMethod, not HighComplexity CogCC
    [Fact]
    public void HighComplexity_CogCC25_ExcludedInFavorOfLongMethod()
    {
        var methods = new List<MethodMetrics> { new("M", 25, 1, 1, 1, 10) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.Contains(smells, s => s.Kind == CodeSmellKind.LongMethod);
        // HighComplexity should not include CogCC part when CogCC >= 25
        Assert.DoesNotContain(smells, s => s.Kind == CodeSmellKind.HighComplexity);
    }

    // LongMethod CogCC boundary: 24 no (lines=10), 25 yes
    [Fact]
    public void LongMethod_CogCC24_ShortLines_NoSmell()
    {
        var methods = new List<MethodMetrics> { new("M", 24, 1, 1, 1, 10) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.DoesNotContain(smells, s => s.Kind == CodeSmellKind.LongMethod);
    }

    [Fact]
    public void LongMethod_CogCC25_ShortLines_Detected()
    {
        var methods = new List<MethodMetrics> { new("M", 25, 1, 1, 1, 10) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.Contains(smells, s => s.Kind == CodeSmellKind.LongMethod);
    }

    // LongMethod Critical: lines < 150 & cogCC < 40 → Warning; lines >= 150 → Critical; cogCC >= 40 → Critical
    [Fact]
    public void LongMethod_Lines149_CogCC24_Warning()
    {
        var methods = new List<MethodMetrics> { new("M", 24, 1, 1, 1, 149) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        var smell = Assert.Single(smells, s => s.Kind == CodeSmellKind.LongMethod);
        Assert.Equal(SmellSeverity.Warning, smell.Severity);
    }

    [Fact]
    public void LongMethod_Lines150_Critical()
    {
        var methods = new List<MethodMetrics> { new("M", 1, 1, 1, 1, 150) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        var smell = Assert.Single(smells, s => s.Kind == CodeSmellKind.LongMethod);
        Assert.Equal(SmellSeverity.Critical, smell.Severity);
    }

    [Fact]
    public void LongMethod_CogCC40_Critical()
    {
        var methods = new List<MethodMetrics> { new("M", 40, 1, 1, 1, 10) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        var smell = Assert.Single(smells, s => s.Kind == CodeSmellKind.LongMethod);
        Assert.Equal(SmellSeverity.Critical, smell.Severity);
    }

    // LowMaintainability: MI=20.0 no smell, MI=19.9 Warning, MI=10.0 Warning, MI=9.9 Critical
    [Fact]
    public void LowMaintainability_MI20_NoSmell()
    {
        var methods = new List<MethodMetrics> { new("M", 1, 1, 1, 1, 10, MaintainabilityIndex: 20.0) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.DoesNotContain(smells, s => s.Kind == CodeSmellKind.LowMaintainability);
    }

    [Fact]
    public void LowMaintainability_MI19_9_Warning()
    {
        var methods = new List<MethodMetrics> { new("M", 1, 1, 1, 1, 10, MaintainabilityIndex: 19.9) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        var smell = Assert.Single(smells, s => s.Kind == CodeSmellKind.LowMaintainability);
        Assert.Equal(SmellSeverity.Warning, smell.Severity);
    }

    [Fact]
    public void LowMaintainability_MI10_Warning()
    {
        var methods = new List<MethodMetrics> { new("M", 1, 1, 1, 1, 10, MaintainabilityIndex: 10.0) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        var smell = Assert.Single(smells, s => s.Kind == CodeSmellKind.LowMaintainability);
        Assert.Equal(SmellSeverity.Warning, smell.Severity);
    }

    [Fact]
    public void LowMaintainability_MI9_9_Critical()
    {
        var methods = new List<MethodMetrics> { new("M", 1, 1, 1, 1, 10, MaintainabilityIndex: 9.9) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        var smell = Assert.Single(smells, s => s.Kind == CodeSmellKind.LowMaintainability);
        Assert.Equal(SmellSeverity.Critical, smell.Severity);
    }

    // DeepInheritance: dit=5 no, dit=6 yes
    [Fact]
    public void DeepInheritance_Dit5_NoSmell()
    {
        var metrics = MakeTypeMetrics();
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null, dit: 5);
        Assert.DoesNotContain(smells, s => s.Kind == CodeSmellKind.DeepInheritance);
    }

    [Fact]
    public void DeepInheritance_Dit6_Detected()
    {
        var metrics = MakeTypeMetrics();
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null, dit: 6);
        Assert.Contains(smells, s => s.Kind == CodeSmellKind.DeepInheritance);
    }

    // HighCoupling: cbo=13 no, cbo=14 Warning, cbo=24 Warning, cbo=25 Critical
    [Fact]
    public void HighCoupling_Cbo13_NoSmell()
    {
        var metrics = MakeTypeMetrics();
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null, cbo: 13);
        Assert.DoesNotContain(smells, s => s.Kind == CodeSmellKind.HighCoupling);
    }

    [Fact]
    public void HighCoupling_Cbo14_Warning()
    {
        var metrics = MakeTypeMetrics();
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null, cbo: 14);
        var smell = Assert.Single(smells, s => s.Kind == CodeSmellKind.HighCoupling);
        Assert.Equal(SmellSeverity.Warning, smell.Severity);
    }

    [Fact]
    public void HighCoupling_Cbo24_Warning()
    {
        var metrics = MakeTypeMetrics();
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null, cbo: 24);
        var smell = Assert.Single(smells, s => s.Kind == CodeSmellKind.HighCoupling);
        Assert.Equal(SmellSeverity.Warning, smell.Severity);
    }

    [Fact]
    public void HighCoupling_Cbo25_Critical()
    {
        var metrics = MakeTypeMetrics();
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null, cbo: 25);
        var smell = Assert.Single(smells, s => s.Kind == CodeSmellKind.HighCoupling);
        Assert.Equal(SmellSeverity.Critical, smell.Severity);
    }

    // DeepNesting: depth=3 no smell
    [Fact]
    public void DeepNesting_Depth3_NoSmell()
    {
        var methods = new List<MethodMetrics> { new("M", 1, 1, 3, 1, 10) };
        var metrics = MakeTypeMetrics(methods: methods);
        var typeInfo = MakeTypeInfo();
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        Assert.DoesNotContain(smells, s => s.Kind == CodeSmellKind.DeepNesting);
    }

    // GodClass Critical boundary: lines=999 + methods=20 → Warning, lines=1000 + methods=20 → Critical
    [Fact]
    public void GodClass_Lines999_Methods20_Warning()
    {
        var metrics = MakeTypeMetrics(lineCount: 999, methodCount: 20);
        var typeInfo = MakeTypeInfo(lineCount: 999);
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        var godClass = Assert.Single(smells, s => s.Kind == CodeSmellKind.GodClass);
        Assert.Equal(SmellSeverity.Warning, godClass.Severity);
    }

    [Fact]
    public void GodClass_Lines1000_Methods20_Critical()
    {
        var metrics = MakeTypeMetrics(lineCount: 1000, methodCount: 20);
        var typeInfo = MakeTypeInfo(lineCount: 1000);
        var smells = CodeSmellDetector.Detect(metrics, typeInfo, lcom: null);
        var godClass = Assert.Single(smells, s => s.Kind == CodeSmellKind.GodClass);
        Assert.Equal(SmellSeverity.Critical, godClass.Severity);
    }
}
