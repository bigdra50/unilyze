using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests.Detectors;

public class CodeSmellLineTests
{
    const string BoxingCode = """
        class C {
            void Foo() {
                object o = 42;
            }
        }
        """;

    static (TypeDeclarationSyntax TypeDecl, SemanticModel Model) ParseType(string code, string typeName = "C")
    {
        var model = RoslynTestHelper.CreateSemanticModel(code);
        var typeDecl = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == typeName);
        return (typeDecl, model);
    }

    static DetectedSmell DetectBoxingSmell()
    {
        var (typeDecl, model) = ParseType(BoxingCode);
        return new BoxingSmellDetector().Detect(typeDecl, model).Single();
    }

    static TypeMetrics MakeTypeMetricsWithSmell(CodeSmell smell, int methodStartLine)
    {
        return new TypeMetrics(
            smell.TypeName, "", "TestAssembly",
            20, 1, 1,
            1.0, 1, 1.0, 1,
            0, 8.0,
            [new MethodMetrics("Foo", 1, 1, 1, 0, 5, StartLine: methodStartLine)],
            CodeSmells: [smell],
            FilePath: "/project/src/Test.cs",
            StartLine: 1);
    }

    [Fact]
    public void BoxingSmell_SarifUsesOccurrenceLine()
    {
        var (typeDecl, model) = ParseType(BoxingCode);
        var boxing = BoxingDetector.Detect(typeDecl, model).Single();
        var detected = new BoxingSmellDetector().Detect(typeDecl, model).Single();
        var methodStartLine = RoslynTestHelper.GetMethod(BoxingCode, "Foo")
            .GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        Assert.NotEqual(methodStartLine, boxing.Line);

        var smell = new CodeSmell(
            detected.Kind, detected.Severity, detected.TypeName,
            detected.MethodName, detected.Message, detected.Line);
        var typeMetrics = MakeTypeMetricsWithSmell(smell, methodStartLine);
        var result = new AnalysisResult(
            "/project", DateTimeOffset.UtcNow, [], [], [], [typeMetrics]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var region = doc["runs"]![0]!["results"]![0]!["locations"]![0]!
            ["physicalLocation"]!["region"]!;

        Assert.Equal(boxing.Line, region["startLine"]!.GetValue<int>());
    }

    [Fact]
    public void FeatureSmell_JsonIncludesLine()
    {
        var detected = DetectBoxingSmell();
        var smell = new CodeSmell(
            detected.Kind, detected.Severity, detected.TypeName,
            detected.MethodName, detected.Message, detected.Line);
        var typeMetrics = MakeTypeMetricsWithSmell(smell, methodStartLine: 2);
        var result = new AnalysisResult(
            "/project", DateTimeOffset.UtcNow, [], [], [], [typeMetrics]);

        var json = JsonSerializer.Serialize(result, AnalysisJsonContext.Default.AnalysisResult);
        var doc = JsonNode.Parse(json)!;
        var line = doc["typeMetrics"]![0]!["codeSmells"]![0]!["line"]!.GetValue<int>();

        Assert.Equal(detected.Line, line);
    }

    [Fact]
    public void FeatureSmell_MessagesMatchLegacyFormat()
    {
        var (typeDecl, model) = ParseType(BoxingCode);
        var boxing = BoxingDetector.Detect(typeDecl, model).Single();
        var detected = new BoxingSmellDetector().Detect(typeDecl, model).Single();

        Assert.Equal(
            (CodeSmellKind.BoxingAllocation, boxing.MethodName, boxing.Description),
            (detected.Kind, detected.MethodName, detected.Message));
        Assert.NotNull(detected.Line);
    }

    [Fact]
    public void IdenticalSnapshots_NoSmellChangesDespiteLine()
    {
        var detected = DetectBoxingSmell();
        var smell = new CodeSmell(
            detected.Kind, detected.Severity, detected.TypeName,
            detected.MethodName, detected.Message, detected.Line);
        var typeMetrics = MakeTypeMetricsWithSmell(smell, methodStartLine: 2);
        var snapshot = new AnalysisResult(
            "/project", DateTimeOffset.UtcNow, [], [], [], [typeMetrics]);

        var diff = DiffCalculator.Compare(snapshot, snapshot);

        Assert.Empty(diff.Improved);
        Assert.Empty(diff.Degraded);
        Assert.Single(diff.Unchanged);
        Assert.Null(diff.Unchanged[0].SmellChanges);
    }

    [Fact]
    public void SmellWithLine_FallsBackToMethodStartLineWhenLineNull()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.DeepNesting, SmellSeverity.Warning, "C", "Foo", "nesting depth 5")
        };
        var typeMetrics = new TypeMetrics(
            "C", "", "TestAssembly",
            20, 1, 5,
            1.0, 8, 1.0, 1,
            0, 8.0,
            [new MethodMetrics("Foo", 1, 8, 5, 0, 5, StartLine: 99)],
            CodeSmells: smells,
            FilePath: "/project/src/Test.cs",
            StartLine: 1);
        var result = new AnalysisResult(
            "/project", DateTimeOffset.UtcNow, [], [], [], [typeMetrics]);

        var json = SarifFormatter.Generate(result);
        var doc = JsonNode.Parse(json)!;
        var region = doc["runs"]![0]!["results"]![0]!["locations"]![0]!
            ["physicalLocation"]!["region"]!;

        Assert.Equal(99, region["startLine"]!.GetValue<int>());
    }
}
