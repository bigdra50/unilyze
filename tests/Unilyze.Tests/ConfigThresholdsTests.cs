using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unilyze;

namespace Unilyze.Tests;

public sealed class ConfigThresholdsTests : IDisposable
{
    readonly string _tempDir;
    readonly StringWriter _stderr;
    readonly TextWriter _originalStderr;

    public ConfigThresholdsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-thresholds-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _stderr = new StringWriter();
        _originalStderr = Console.Error;
    }

    public void Dispose()
    {
        Console.SetError(_originalStderr);
        _stderr.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    void CaptureStderr()
    {
        Console.SetError(_stderr);
    }

    static Dictionary<string, IReadOnlyDictionary<string, JsonElement>> SmellOverrides(
        params (string Smell, string Key, int Value)[] entries)
    {
        var map = new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var group in entries.GroupBy(e => e.Smell, StringComparer.OrdinalIgnoreCase))
        {
            var inner = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in group)
                inner[entry.Key] = JsonSerializer.SerializeToElement(entry.Value);
            map[group.Key] = inner;
        }
        return map;
    }

    static Dictionary<string, string> RuleOverrides(params (string RuleId, string State)[] entries)
        => entries.ToDictionary(e => e.RuleId, e => e.State, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Merge_Smells_ProjectInnerKeyOverridesGlobal_UnrelatedGlobalSurvives()
    {
        var global = new UnilyzeConfig(Smells: SmellOverrides(
            ("LongMethod", "lines", 100),
            ("GodClass", "lines", 600)));
        var project = new UnilyzeConfig(Smells: SmellOverrides(
            ("LongMethod", "lines", 40),
            ("LongMethod", "criticalLines", 120)));

        var merged = UnilyzeConfig.Merge(global, project);
        var thresholds = EffectiveSmellThresholds.FromOverrides(merged.Smells);

        Assert.Equal(40, thresholds.LongMethodLinesWarning);
        Assert.Equal(120, thresholds.LongMethodLinesCritical);
        Assert.Equal(600, thresholds.GodClassLinesWarning);
        Assert.Equal(SmellThresholds.LongMethodCogCcWarning, thresholds.LongMethodCogCcWarning);
    }

    [Fact]
    public void Merge_Rules_ProjectKeyOverridesGlobal_UnrelatedGlobalSurvives()
    {
        var global = new UnilyzeConfig(Rules: RuleOverrides(
            ("UNI011", "off"),
            ("UNI003", "off")));
        var project = new UnilyzeConfig(Rules: RuleOverrides(
            ("UNI011", "on"),
            ("UNI009", "off")));

        var merged = UnilyzeConfig.Merge(global, project);
        var resolved = merged.ResolveAnalysisConfig();

        Assert.False(resolved.DisabledRuleKinds.Contains(CodeSmellKind.BoxingAllocation));
        Assert.True(resolved.DisabledRuleKinds.Contains(CodeSmellKind.ExcessiveParameters));
        Assert.True(resolved.DisableCycles);
    }

    [Fact]
    public void ThresholdOverride_LongMethodLines_ChangesDetection()
    {
        var methods = new List<MethodMetrics>
        {
            new("Medium", 5, 10, 2, 3, 50)
        };
        var metrics = new TypeMetrics(
            "TestClass", "TestNs", "TestAsm",
            100, 5, 1, 3.0, 5, 3.0, 5, 0, 8.0, methods);
        var typeInfo = new TypeNodeInfo(
            "TestClass", "TestNs", "class",
            ["public"], null, [], [], [], [], [], null,
            "TestAsm", "test.cs", false, 100);

        var defaultSmells = CodeSmellDetector.Detect(metrics, typeInfo, null);
        var lowered = EffectiveSmellThresholds.Default with { LongMethodLinesWarning = 40 };
        var overriddenSmells = CodeSmellDetector.Detect(metrics, typeInfo, null, thresholds: lowered);

        Assert.DoesNotContain(defaultSmells, s => s.Kind == CodeSmellKind.LongMethod);
        Assert.Contains(overriddenSmells, s => s.Kind == CodeSmellKind.LongMethod);
    }

    [Fact]
    public void DisabledRule_UNI011_RemovesBoxingSmellsAndNullsBoxingCount()
    {
        WriteFile("Types.cs", """
            public class PlainClass
            {
                public void Box() { object o = 42; }
            }
            """);

        var enabled = EnrichFromDisk(new HashSet<CodeSmellKind>());
        var enabledMetrics = enabled.Single(m => m.TypeName == "PlainClass");
        Assert.NotNull(enabledMetrics.BoxingCount);
        Assert.Contains(
            enabledMetrics.CodeSmells ?? [],
            s => s.Kind == CodeSmellKind.BoxingAllocation);

        var config = new UnilyzeConfig(Rules: RuleOverrides(("UNI011", "off")));
        var resolved = config.ResolveAnalysisConfig();
        var disabled = EnrichFromDisk(resolved.DisabledRuleKinds);
        var disabledMetrics = disabled.Single(m => m.TypeName == "PlainClass");

        Assert.Null(disabledMetrics.BoxingCount);
        Assert.DoesNotContain(
            disabledMetrics.CodeSmells ?? [],
            s => s.Kind == CodeSmellKind.BoxingAllocation);
    }

    [Fact]
    public void DisabledRule_UNI009_SetsCyclicDependenciesNull()
    {
        WriteFile("Types.cs", """
            public class A { public B Field; }
            public class B { public A Field; }
            """);
        WriteFile(".unilyze.json", """
            {
                "disableDefaultExcludes": true,
                "rules": { "UNI009": "off" }
            }
            """);

        var config = UnilyzeConfig.LoadMerged(_tempDir);
        var resolved = config.ResolveAnalysisConfig();
        var result = AnalysisPipeline.Build(
            _tempDir, null, null, config.ExcludeDirs,
            excludeGeneratedCode: false,
            applyAnyDepthExcludes: false,
            thresholds: resolved.Thresholds,
            disabledRuleKinds: resolved.DisabledRuleKinds,
            disableCycles: resolved.DisableCycles);

        Assert.Null(result.CyclicDependencies);
    }

    [Fact]
    public void NoConfig_UsesDefaultsAndCurrentMetricsVersion()
    {
        WriteFile("Types.cs", """
            public class Healthy { public int Value; }
            """);

        var config = UnilyzeConfig.LoadMerged(_tempDir);
        var resolved = config.ResolveAnalysisConfig();
        var result = AnalysisPipeline.Build(
            _tempDir, null, null, config.ExcludeDirs,
            thresholds: resolved.Thresholds,
            disabledRuleKinds: resolved.DisabledRuleKinds,
            disableCycles: resolved.DisableCycles);

        Assert.Equal(AnalysisResult.CurrentMetricsVersion, result.MetricsVersion);
        Assert.Same(EffectiveSmellThresholds.Default, resolved.Thresholds);
        Assert.Empty(resolved.DisabledRuleKinds);
        Assert.False(resolved.DisableCycles);
    }

    [Fact]
    public void UnknownSmellThresholdAndRule_WarnAndIgnore_DoesNotFail()
    {
        CaptureStderr();
        var smells = SmellOverrides(
            ("UnknownSmell", "lines", 10),
            ("LongMethod", "unknownKey", 10));
        var rules = RuleOverrides(("UNI999", "off"), ("UNI011", "maybe"));

        var thresholds = EffectiveSmellThresholds.FromOverrides(smells);
        var resolved = new UnilyzeConfig(Rules: rules).ResolveAnalysisConfig();

        Assert.Same(EffectiveSmellThresholds.Default, thresholds);
        Assert.Empty(resolved.DisabledRuleKinds);
        Assert.False(resolved.DisableCycles);

        var stderr = _stderr.ToString();
        Assert.Contains("Unknown smell name 'UnknownSmell'", stderr);
        Assert.Contains("Unknown threshold key 'unknownKey'", stderr);
        Assert.Contains("Unknown rule id 'UNI999'", stderr);
        Assert.Contains("Unknown rule state 'maybe'", stderr);
    }

    IReadOnlyList<TypeMetrics> EnrichFromDisk(IReadOnlySet<CodeSmellKind> disabledRuleKinds)
    {
        var analyzed = TypeAnalyzer.AnalyzeDirectoryWithTrees(_tempDir, "TestAsm");
        var compilation = CSharpCompilation.Create(
            "TestAsm",
            analyzed.SyntaxTrees,
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
        var compilationResult = new CompilationResult(compilation, AnalysisLevel.Core);
        var allTypes = BaseTypeResolver
            .ResolveTypeRelationships(analyzed.Types, analyzed.SyntaxTrees, compilationResult)
            .ToList();
        var typeMetrics = CodeHealthCalculator.ComputeTypeMetrics(allTypes);

        return SemanticEnricher.Enrich(
            typeMetrics, allTypes, analyzed.SyntaxTrees, compilationResult,
            disabledRuleKinds: disabledRuleKinds);
    }

    void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
