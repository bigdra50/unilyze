using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests.Detectors;

public sealed class SuppressionDirectiveScannerTests
{
    [Theory]
    [InlineData("// unilyze-disable-next-line UNI014", true, "UNI014")]
    [InlineData("// unilyze-disable UNI002, UNI003", false, "UNI002")]
    [InlineData("// unilyze-disable-next-line UNI011 UNI012", true, "UNI011")]
    [InlineData("// unilyze-disable", false, null)]
    public void TryParseDirective_ParsesRuleLists(string comment, bool nextLine, string? expectedRule)
    {
        Assert.True(SuppressionIndex.TryParseDirective(comment, out var isNextLine, out var entry));
        Assert.Equal(nextLine, isNextLine);
        if (expectedRule is null)
            Assert.Null(entry.Kinds);
        else
            Assert.Contains(SarifFormatter.TryGetKind(expectedRule, out var kind) ? kind : default, entry.Kinds!);
    }

    [Fact]
    public void TryParseDirective_CapturesJustificationSuffix()
    {
        Assert.True(SuppressionIndex.TryParseDirective(
            "// unilyze-disable-next-line UNI014 -- intentional guard",
            out _,
            out var entry));

        Assert.Equal("intentional guard", entry.Justification);
    }

    [Fact]
    public void Build_NextLineTargetsFollowingLine()
    {
        const string code = """
            class Sample
            {
                void M()
                {
                    try { }
                    // unilyze-disable-next-line UNI014
                    catch { }
                }
            }
            """;

        var typeDecl = ParseType(code);
        var index = SuppressionIndex.Build(typeDecl);

        var smell = new DetectedSmell(
            CodeSmellKind.CatchAllException,
            SmellSeverity.Warning,
            "Sample",
            "M",
            "catch-all",
            Line: LineOf(code, "catch { }"));

        Assert.True(index.IsDetectorSmellSuppressed(smell, out var justification));
        Assert.Null(justification);
    }

    [Fact]
    public void TryParseDirective_UnknownRuleId_WarnsAndIgnoresRule()
    {
        using var stderr = new StringWriter();
        var original = Console.Error;
        Console.SetError(stderr);
        try
        {
            Assert.True(SuppressionIndex.TryParseDirective(
                "// unilyze-disable UNI999",
                out _,
                out var entry));
            Assert.Null(entry.Kinds);
            Assert.Contains("Unknown rule id 'UNI999'", stderr.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public void TryParseDirective_UNI009_WarnsAndIsIgnored()
    {
        using var stderr = new StringWriter();
        var original = Console.Error;
        Console.SetError(stderr);
        try
        {
            Assert.True(SuppressionIndex.TryParseDirective(
                "// unilyze-disable UNI009",
                out _,
                out var entry));
            Assert.Null(entry.Kinds);
            Assert.Contains("UNI009", stderr.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    static TypeDeclarationSyntax ParseType(string code)
    {
        var model = RoslynTestHelper.CreateSemanticModel(code);
        return model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First();
    }

    static int LineOf(string code, string snippet)
    {
        var lineIndex = code.Split('\n').Select((line, index) => (line, index))
            .First(pair => pair.line.Contains(snippet, StringComparison.Ordinal))
            .index;
        return lineIndex + 1;
    }
}
