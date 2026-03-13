using System.Text.Json;
using System.Text.Json.Nodes;

namespace Unilyze.Tests;

public class SarifFormatterTests
{
    static AnalysisResult MakeResult(
        IReadOnlyList<TypeMetrics>? typeMetrics = null,
        string projectPath = "/project")
    {
        return new AnalysisResult(
            projectPath,
            DateTimeOffset.UtcNow,
            [],
            [],
            [],
            typeMetrics);
    }

    static TypeMetrics MakeTypeMetrics(
        string typeName = "TestClass",
        string ns = "TestNs",
        int lineCount = 100,
        int methodCount = 5,
        IReadOnlyList<MethodMetrics>? methods = null,
        IReadOnlyList<CodeSmell>? smells = null,
        string? filePath = "/project/src/Test.cs",
        int? startLine = 10)
    {
        methods ??= [];
        return new TypeMetrics(
            typeName, ns, "TestAssembly",
            lineCount, methodCount, 1,
            3.0, 5, 3.0, 5,
            0, 8.0,
            methods,
            CodeSmells: smells,
            FilePath: filePath,
            StartLine: startLine);
    }

    [Fact]
    public void EmptyResult_ProducesValidSarif()
    {
        var result = MakeResult();

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;

        Assert.Equal("2.1.0", doc["version"]?.GetValue<string>());
        Assert.NotNull(doc["$schema"]);

        var runs = doc["runs"]!.AsArray();
        Assert.Single(runs);

        var run = runs[0]!;
        Assert.Equal("unilyze", run["tool"]!["driver"]!["name"]!.GetValue<string>());

        var results = run["results"]!.AsArray();
        Assert.Empty(results);
    }

    [Fact]
    public void SingleCodeSmell_ProducesCorrectResult()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.GodClass, SmellSeverity.Warning, "BigClass", null, "600 lines, 25 methods")
        };
        var typeMetrics = MakeTypeMetrics(
            typeName: "BigClass", lineCount: 600, methodCount: 25, smells: smells);
        var result = MakeResult([typeMetrics]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var results = doc["runs"]![0]!["results"]!.AsArray();

        Assert.Single(results);

        var r = results[0]!;
        Assert.Equal("UNI001", r["ruleId"]!.GetValue<string>());
        Assert.Equal("warning", r["level"]!.GetValue<string>());
        Assert.Contains("BigClass", r["message"]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void CriticalSeverity_MapsToError()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.LongMethod, SmellSeverity.Critical, "TestClass", "HugeMethod", "200 lines")
        };
        var methods = new List<MethodMetrics>
        {
            new("HugeMethod", 50, 20, 5, 3, 200, StartLine: 42)
        };
        var typeMetrics = MakeTypeMetrics(methods: methods, smells: smells);
        var result = MakeResult([typeMetrics]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var r = doc["runs"]![0]!["results"]![0]!;

        Assert.Equal("error", r["level"]!.GetValue<string>());
        Assert.Equal("UNI002", r["ruleId"]!.GetValue<string>());
    }

    [Fact]
    public void MethodLevelSmell_UsesMethodStartLine()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.DeepNesting, SmellSeverity.Warning, "TestClass", "DeepMethod", "nesting depth 5")
        };
        var methods = new List<MethodMetrics>
        {
            new("DeepMethod", 10, 8, 5, 2, 30, StartLine: 77)
        };
        var typeMetrics = MakeTypeMetrics(
            methods: methods, smells: smells, startLine: 10);
        var result = MakeResult([typeMetrics]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var location = doc["runs"]![0]!["results"]![0]!["locations"]![0]!;
        var region = location["physicalLocation"]!["region"]!;

        Assert.Equal(77, region["startLine"]!.GetValue<int>());
    }

    [Fact]
    public void TypeLevelSmell_UsesTypeStartLine()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.LowCohesion, SmellSeverity.Warning, "TestClass", null, "LCOM 0.85")
        };
        var typeMetrics = MakeTypeMetrics(smells: smells, startLine: 15);
        var result = MakeResult([typeMetrics]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var location = doc["runs"]![0]!["results"]![0]!["locations"]![0]!;
        var region = location["physicalLocation"]!["region"]!;

        Assert.Equal(15, region["startLine"]!.GetValue<int>());
    }

    [Fact]
    public void PathsAreRelative()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.GodClass, SmellSeverity.Warning, "TestClass", null, "big")
        };
        var typeMetrics = MakeTypeMetrics(
            smells: smells, filePath: "/project/src/Deep/Test.cs");
        var result = MakeResult([typeMetrics], projectPath: "/project");

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var uri = doc["runs"]![0]!["results"]![0]!
            ["locations"]![0]!["physicalLocation"]!["artifactLocation"]!["uri"]!.GetValue<string>();

        Assert.Equal("src/Deep/Test.cs", uri);
    }

    [Fact]
    public void NoFilePath_OmitsLocations()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.GodClass, SmellSeverity.Warning, "TestClass", null, "big")
        };
        var typeMetrics = MakeTypeMetrics(smells: smells, filePath: null, startLine: null);
        var result = MakeResult([typeMetrics]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var r = doc["runs"]![0]!["results"]![0]!;

        Assert.Null(r["locations"]);
    }

    [Fact]
    public void AllRulesAreDefined()
    {
        var result = MakeResult();
        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var rules = doc["runs"]![0]!["tool"]!["driver"]!["rules"]!.AsArray();

        Assert.Equal(10, rules.Count);
        var ruleIds = rules.Select(r => r!["id"]!.GetValue<string>()).ToList();
        Assert.Contains("UNI001", ruleIds);
        Assert.Contains("UNI002", ruleIds);
        Assert.Contains("UNI003", ruleIds);
        Assert.Contains("UNI004", ruleIds);
        Assert.Contains("UNI005", ruleIds);
        Assert.Contains("UNI006", ruleIds);
        Assert.Contains("UNI007", ruleIds);
        Assert.Contains("UNI008", ruleIds);
        Assert.Contains("UNI009", ruleIds);
        Assert.Contains("UNI010", ruleIds);
    }

    [Fact]
    public void MultipleSmells_AllPresent()
    {
        var smells1 = new List<CodeSmell>
        {
            new(CodeSmellKind.GodClass, SmellSeverity.Warning, "ClassA", null, "big"),
            new(CodeSmellKind.LowCohesion, SmellSeverity.Warning, "ClassA", null, "LCOM 0.9"),
        };
        var smells2 = new List<CodeSmell>
        {
            new(CodeSmellKind.HighComplexity, SmellSeverity.Warning, "ClassB", "ComplexMethod", "cyclomatic CC 20"),
        };
        var metrics1 = MakeTypeMetrics(typeName: "ClassA", smells: smells1);
        var metrics2 = MakeTypeMetrics(typeName: "ClassB", smells: smells2,
            methods: [new MethodMetrics("ComplexMethod", 20, 20, 3, 2, 50, StartLine: 30)]);
        var result = MakeResult([metrics1, metrics2]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var results = doc["runs"]![0]!["results"]!.AsArray();

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void MethodSmell_HasMetricsInProperties()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.HighComplexity, SmellSeverity.Warning, "TestClass", "Foo", "cyclomatic CC 20")
        };
        var methods = new List<MethodMetrics>
        {
            new("Foo", 12, 20, 3, 4, 60, StartLine: 50)
        };
        var typeMetrics = MakeTypeMetrics(methods: methods, smells: smells);
        var result = MakeResult([typeMetrics]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var props = doc["runs"]![0]!["results"]![0]!["properties"]!;

        Assert.Equal("Foo", props["methodName"]!.GetValue<string>());
        Assert.Equal(12, props["cognitiveComplexity"]!.GetValue<int>());
        Assert.Equal(20, props["cyclomaticComplexity"]!.GetValue<int>());
    }

    [Fact]
    public void ExcessiveParameters_MapsToUNI003()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.ExcessiveParameters, SmellSeverity.Warning,
                "TestClass", "TooManyArgs", "8 parameters (threshold: 5)")
        };
        var methods = new List<MethodMetrics>
        {
            new("TooManyArgs", 3, 2, 1, 8, 20, StartLine: 30)
        };
        var typeMetrics = MakeTypeMetrics(methods: methods, smells: smells);
        var result = MakeResult([typeMetrics]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var r = doc["runs"]![0]!["results"]![0]!;

        Assert.Equal("UNI003", r["ruleId"]!.GetValue<string>());
        Assert.Equal("warning", r["level"]!.GetValue<string>());
        Assert.Contains("TooManyArgs", r["message"]!["text"]!.GetValue<string>());

        var props = r["properties"]!;
        Assert.Equal("TooManyArgs", props["methodName"]!.GetValue<string>());
        Assert.Equal(8, props["parameterCount"]!.GetValue<int>());
    }

    [Fact]
    public void HighCoupling_MapsToUNI007()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.HighCoupling, SmellSeverity.Warning,
                "HubClass", null, "CBO 30 (threshold: 20)")
        };
        var typeMetrics = MakeTypeMetrics(
            typeName: "HubClass", lineCount: 400, methodCount: 15, smells: smells);
        var result = MakeResult([typeMetrics]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var r = doc["runs"]![0]!["results"]![0]!;

        Assert.Equal("UNI007", r["ruleId"]!.GetValue<string>());
        Assert.Equal("warning", r["level"]!.GetValue<string>());
        Assert.Contains("HubClass", r["message"]!["text"]!.GetValue<string>());

        var props = r["properties"]!;
        Assert.Equal(400, props["lineCount"]!.GetValue<int>());
        Assert.Equal(15, props["methodCount"]!.GetValue<int>());
    }

    [Fact]
    public void LowMaintainability_MapsToUNI008()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.LowMaintainability, SmellSeverity.Warning,
                "TestClass", "HardToMaintain", "MI 35.0 (threshold: 40)")
        };
        var methods = new List<MethodMetrics>
        {
            new("HardToMaintain", 15, 12, 4, 3, 80, StartLine: 60, MaintainabilityIndex: 35.0)
        };
        var typeMetrics = MakeTypeMetrics(methods: methods, smells: smells);
        var result = MakeResult([typeMetrics]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var r = doc["runs"]![0]!["results"]![0]!;

        Assert.Equal("UNI008", r["ruleId"]!.GetValue<string>());
        Assert.Equal("warning", r["level"]!.GetValue<string>());
        Assert.Contains("HardToMaintain", r["message"]!["text"]!.GetValue<string>());

        var props = r["properties"]!;
        Assert.Equal("HardToMaintain", props["methodName"]!.GetValue<string>());
        Assert.Equal(80, props["methodLineCount"]!.GetValue<int>());
    }

    [Fact]
    public void CyclicDependency_MapsToUNI009()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.CyclicDependency, SmellSeverity.Warning,
                "CycleClass", null, "involved in cyclic dependency")
        };
        var typeMetrics = MakeTypeMetrics(typeName: "CycleClass", smells: smells);
        var result = MakeResult([typeMetrics]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var r = doc["runs"]![0]!["results"]![0]!;

        Assert.Equal("UNI009", r["ruleId"]!.GetValue<string>());
        Assert.Equal("warning", r["level"]!.GetValue<string>());
        Assert.Contains("CycleClass", r["message"]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void DeepInheritance_MapsToUNI010()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.DeepInheritance, SmellSeverity.Warning,
                "DeeplyNested", null, "DIT 6 (threshold: 4)")
        };
        var typeMetrics = MakeTypeMetrics(typeName: "DeeplyNested", smells: smells) with { Dit = 6 };
        var result = MakeResult([typeMetrics]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var r = doc["runs"]![0]!["results"]![0]!;

        Assert.Equal("UNI010", r["ruleId"]!.GetValue<string>());
        Assert.Equal("warning", r["level"]!.GetValue<string>());
        Assert.Contains("DeeplyNested", r["message"]!["text"]!.GetValue<string>());

        var props = r["properties"]!;
        Assert.Equal(100, props["lineCount"]!.GetValue<int>());
        Assert.Equal(5, props["methodCount"]!.GetValue<int>());
    }

    [Fact]
    public void TypeMetrics_NullCodeSmells_Skipped()
    {
        var typeMetrics = MakeTypeMetrics(smells: null);
        var result = MakeResult([typeMetrics]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var results = doc["runs"]![0]!["results"]!.AsArray();

        Assert.Empty(results);
    }

    [Fact]
    public void TypeLevelSmell_HasLcomInProperties()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.LowCohesion, SmellSeverity.Warning,
                "ScatteredClass", null, "LCOM 0.92")
        };
        var typeMetrics = MakeTypeMetrics(typeName: "ScatteredClass", smells: smells) with { Lcom = 0.92 };
        var result = MakeResult([typeMetrics]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var props = doc["runs"]![0]!["results"]![0]!["properties"]!;

        Assert.Equal(0.92, props["lcom"]!.GetValue<double>(), precision: 2);
        Assert.Equal(100, props["lineCount"]!.GetValue<int>());
        Assert.Equal(5, props["methodCount"]!.GetValue<int>());
    }
}
