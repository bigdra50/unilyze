using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze.Tests;

public sealed class HotPathEscalationTests : IDisposable
{
    readonly string _tempDir;

    const string UnityStub = """
        namespace UnityEngine
        {
            public class MonoBehaviour { }
        }
        """;

    public HotPathEscalationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-hotpath-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Enrich_BoxingInUpdate_EscalatesToCritical()
    {
        WriteSources(UnityStub + """
            using UnityEngine;

            public class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    object o = 42;
                }
            }
            """);

        var metrics = EnrichFromDisk();
        var smell = FindSmell(metrics, "Player", CodeSmellKind.BoxingAllocation, "Update");

        Assert.Equal(SmellSeverity.Critical, smell.Severity);
    }

    [Fact]
    public void Enrich_BoxingInAwake_StaysWarning()
    {
        WriteSources(UnityStub + """
            using UnityEngine;

            public class Player : UnityEngine.MonoBehaviour
            {
                void Awake()
                {
                    object o = 42;
                }
            }
            """);

        var metrics = EnrichFromDisk();
        var smell = FindSmell(metrics, "Player", CodeSmellKind.BoxingAllocation, "Awake");

        Assert.Equal(SmellSeverity.Warning, smell.Severity);
    }

    [Fact]
    public void Enrich_BoxingInPlainClass_StaysWarning()
    {
        WriteSources("""
            public class PlainClass
            {
                void Update()
                {
                    object o = 42;
                }
            }
            """);

        var metrics = EnrichFromDisk();
        var smell = FindSmell(metrics, "PlainClass", CodeSmellKind.BoxingAllocation, "Update");

        Assert.Equal(SmellSeverity.Warning, smell.Severity);
    }

    [Fact]
    public void Enrich_ClosureInCoroutine_EscalatesToCritical()
    {
        WriteSources(UnityStub + """
            using System;
            using System.Collections;
            using UnityEngine;

            public class Player : UnityEngine.MonoBehaviour
            {
                IEnumerator MyCoroutine()
                {
                    int count = 0;
                    Action a = () => Console.WriteLine(count);
                    yield break;
                }
            }
            """);

        var metrics = EnrichFromDisk();
        var smell = FindSmell(metrics, "Player", CodeSmellKind.ClosureCapture, "MyCoroutine");

        Assert.Equal(SmellSeverity.Critical, smell.Severity);
    }

    [Fact]
    public void Enrich_ClosureInCoroutine_SyntaxOnly_EscalatesToCritical()
    {
        WriteSources(UnityStub + """
            using System;
            using System.Collections;
            using UnityEngine;

            public class Player : UnityEngine.MonoBehaviour
            {
                IEnumerator MyCoroutine()
                {
                    int count = 0;
                    Action a = () => Console.WriteLine(count);
                    yield break;
                }
            }
            """);

        var metrics = EnrichFromDisk(syntaxOnly: true);
        var smell = FindSmell(metrics, "Player", CodeSmellKind.ClosureCapture, "MyCoroutine");

        Assert.Equal(SmellSeverity.Critical, smell.Severity);
    }

    [Fact]
    public void Enrich_ParamsInFixedUpdate_EscalatesToCritical()
    {
        WriteSources(UnityStub + """
            using UnityEngine;

            public static class Logger
            {
                public static void Log(params object[] args) { }
            }

            public class Player : UnityEngine.MonoBehaviour
            {
                void FixedUpdate()
                {
                    Logger.Log("a", "b", "c");
                }
            }
            """);

        var metrics = EnrichFromDisk();
        var smell = FindSmell(metrics, "Player", CodeSmellKind.ParamsArrayAllocation, "FixedUpdate");

        Assert.Equal(SmellSeverity.Critical, smell.Severity);
    }

    [Fact]
    public void Enrich_SyntaxOnly_BoxingAndParamsEmitNothing()
    {
        WriteSources(UnityStub + """
            using UnityEngine;

            public static class Logger
            {
                public static void Log(params object[] args) { }
            }

            public class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    object o = 42;
                    Logger.Log("a", "b");
                }
            }
            """);

        var metrics = EnrichFromDisk(syntaxOnly: true);
        var player = FindMetrics(metrics, "Player");

        Assert.Null(player.BoxingCount);
        Assert.Null(player.ParamsAllocationCount);
        Assert.DoesNotContain(
            player.CodeSmells ?? [],
            s => s.Kind is CodeSmellKind.BoxingAllocation or CodeSmellKind.ParamsArrayAllocation);
    }

    [Fact]
    public void Enrich_EscalatedBoxing_SarifLevelIsErrorAndLinePreserved()
    {
        WriteSources(UnityStub + """
            using UnityEngine;

            public class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    object o = 42;
                }
            }
            """);

        var metrics = EnrichFromDisk();
        var smell = FindSmell(metrics, "Player", CodeSmellKind.BoxingAllocation, "Update");
        var result = new AnalysisResult(
            _tempDir, DateTimeOffset.UtcNow, [], [], [], metrics);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var resultNode = doc["runs"]![0]!["results"]!
            .AsArray()
            .Single(r => r!["ruleId"]!.GetValue<string>() == "UNI011");

        Assert.Equal("error", resultNode!["level"]!.GetValue<string>());
        Assert.NotNull(smell.Line);
        Assert.Equal(
            smell.Line,
            resultNode["locations"]![0]!["physicalLocation"]!["region"]!["startLine"]!.GetValue<int>());
    }

    [Fact]
    public void Enrich_HotPathCritical_FailsSmellsGateWithHighFailOver()
    {
        WriteSources(UnityStub + """
            using UnityEngine;

            public class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    object o = 42;
                }
            }
            """);

        var metrics = EnrichFromDisk();
        var result = new AnalysisResult(
            _tempDir, DateTimeOffset.UtcNow, [], [], [], metrics);
        var summary = StatuslineFormatter.ComputeSummary(result);

        var gate = BadgeGate.Evaluate(BadgeMetric.Smells, summary, null, "999");

        Assert.Equal(GateOutcome.Fail, gate.Outcome);
        Assert.Contains("critical smell", gate.Message, StringComparison.OrdinalIgnoreCase);
    }

    void WriteSources(string code)
    {
        foreach (var file in Directory.GetFiles(_tempDir, "*.cs"))
            File.Delete(file);

        File.WriteAllText(Path.Combine(_tempDir, "Types.cs"), code);
    }

    IReadOnlyList<TypeMetrics> EnrichFromDisk(bool syntaxOnly = false)
    {
        var analyzed = TypeAnalyzer.AnalyzeDirectoryWithTrees(_tempDir, "TestAsm");
        CompilationResult compilationResult;

        if (syntaxOnly)
        {
            compilationResult = new CompilationResult(null, AnalysisLevel.Syntax);
        }
        else
        {
            var compilation = CSharpCompilation.Create(
                "TestAsm",
                analyzed.SyntaxTrees,
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithNullableContextOptions(NullableContextOptions.Enable));
            compilationResult = new CompilationResult(compilation, AnalysisLevel.Core);
        }

        var allTypes = BaseTypeResolver
            .ResolveTypeRelationships(analyzed.Types, analyzed.SyntaxTrees, compilationResult)
            .ToList();
        var typeMetrics = CodeHealthCalculator.ComputeTypeMetrics(allTypes);

        return SemanticEnricher.Enrich(
            typeMetrics, allTypes, analyzed.SyntaxTrees, compilationResult);
    }

    static TypeMetrics FindMetrics(IReadOnlyList<TypeMetrics> metrics, string typeName)
        => metrics.Single(m => m.TypeName == typeName);

    static CodeSmell FindSmell(
        IReadOnlyList<TypeMetrics> metrics,
        string typeName,
        CodeSmellKind kind,
        string methodName)
    {
        var typeMetrics = FindMetrics(metrics, typeName);
        Assert.NotNull(typeMetrics.CodeSmells);
        return typeMetrics.CodeSmells!.Single(s => s.Kind == kind && s.MethodName == methodName);
    }
}
