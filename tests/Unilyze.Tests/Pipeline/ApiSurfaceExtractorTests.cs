using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze.Tests.Pipeline;

public sealed class ApiSurfaceExtractorTests
{
    [Fact]
    public void ExtractDocSummary_ParsesMultilineSummary()
    {
        const string source = """
            namespace Test;

            /// <summary>
            ///   First line
            ///   second line
            /// </summary>
            public class DocTarget { }
            """;

        var tree = Parse(source);
        var typeDecl = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>().Single();
        var (hasDoc, summary) = ApiSurfaceExtractor.ExtractDocSummary(typeDecl);

        Assert.True(hasDoc);
        Assert.Equal("First line second line", summary);
    }

    [Fact]
    public void ExtractDocSummary_InheritDoc_HasDocWithoutSummary()
    {
        const string source = """
            namespace Test;

            /// <inheritdoc/>
            public class InheritDocTarget { }
            """;

        var tree = Parse(source);
        var typeDecl = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>().Single();
        var (hasDoc, summary) = ApiSurfaceExtractor.ExtractDocSummary(typeDecl);

        Assert.True(hasDoc);
        Assert.Null(summary);
    }

    [Fact]
    public void ExtractDocSummary_AbsentDocs_ReturnsFalse()
    {
        const string source = """
            namespace Test;

            public class PlainTarget
            {
                public void Run() { }
            }
            """;

        var tree = Parse(source);
        var typeDecl = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>().Single();
        var (hasDoc, summary) = ApiSurfaceExtractor.ExtractDocSummary(typeDecl);

        Assert.False(hasDoc);
        Assert.Null(summary);
    }

    [Fact]
    public void Extract_IncludesPublicSignaturesAndIdentifiers()
    {
        const string source = """
            namespace Test;

            /// <summary>Service docs</summary>
            public class Service
            {
                /// <summary>Runs work</summary>
                public void Execute(int itemId) { }

                private void Hidden() { }
            }
            """;

        var tree = Parse(source);
        var filePath = tree.FilePath;
        var types = TypeAnalyzer.AnalyzeDirectoryWithTrees(
            Path.GetDirectoryName(filePath)!,
            "TestAssembly").Types;

        var surfaces = ApiSurfaceExtractor.Extract([tree], types);
        var service = Assert.Single(surfaces, s => s.QualifiedName == "Test.Service");

        Assert.True(service.HasDocComment);
        Assert.Equal("Service docs", service.DocSummary);
        Assert.Contains("Execute", service.Identifiers);
        Assert.Contains("itemId", service.Identifiers);
        Assert.Contains("Hidden", service.Identifiers);
        Assert.Contains(service.PublicSignatures, s => s.Contains("Execute"));
        Assert.DoesNotContain(service.PublicSignatures, s => s.Contains("Hidden"));
        Assert.Equal(1, service.DocumentedPublicMemberCount);
        Assert.Equal(1, service.PublicMemberCount);
    }

    [Fact]
    public void ExtractDocSummary_TruncatesLongSummary()
    {
        var longText = new string('a', ApiSurfaceExtractor.DocSummaryMaxLength + 50);
        var source = $$"""
            namespace Test;

            /// <summary>{{longText}}</summary>
            public class LongDoc { }
            """;

        var tree = Parse(source);
        var typeDecl = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>().Single();
        var (_, summary) = ApiSurfaceExtractor.ExtractDocSummary(typeDecl);

        Assert.NotNull(summary);
        Assert.Equal(ApiSurfaceExtractor.DocSummaryMaxLength + 1, summary!.Length);
        Assert.EndsWith("…", summary);
    }

    static SyntaxTree Parse(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"unilyze-api-surface-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "Sample.cs");
        File.WriteAllText(filePath, source);
        return CSharpSyntaxTree.ParseText(source, path: filePath);
    }
}
