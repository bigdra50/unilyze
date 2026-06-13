using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze.Tests.Dup;

public sealed class CloneTokenizerTests
{
    static IReadOnlyList<NormalizedToken> TokenizeSource(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Test.cs");
        var sequences = CloneTokenizer.Tokenize([tree]);
        return sequences.Single().Tokens;
    }

    [Fact]
    public void Normalize_IdentifiersBecomeId()
    {
        var tokens = TokenizeSource("class A { void M(int x) { var y = x; } }");
        Assert.Contains(tokens, t => t.Text == "ID");
        Assert.DoesNotContain(tokens, t => t.Text == "x");
        Assert.DoesNotContain(tokens, t => t.Text == "y");
    }

    [Fact]
    public void Normalize_LiteralsBecomeLit()
    {
        var tokens = TokenizeSource("class A { void M() { var x = 42; var s = \"hi\"; var ok = true; } }");
        var litCount = tokens.Count(t => t.Text == "LIT");
        Assert.True(litCount >= 3);
    }

    [Fact]
    public void Normalize_KeywordsRemainVerbatim()
    {
        var tokens = TokenizeSource("class A { void M() { if (true) return; } }");
        Assert.Contains(tokens, t => t.Text == "class");
        Assert.Contains(tokens, t => t.Text == "if");
        Assert.Contains(tokens, t => t.Text == "return");
    }

    [Fact]
    public void Normalize_RenameInvariant_Type2()
    {
        var left = TokenizeSource("class Alpha { void Run(int value) { var total = value + 1; if (total > 0) return; } }");
        var right = TokenizeSource("class Beta { void Go(int amount) { var sum = amount + 1; if (sum > 0) return; } }");
        Assert.Equal(
            left.Select(t => t.Text).ToList(),
            right.Select(t => t.Text).ToList());
    }

    [Fact]
    public void Normalize_WhitespaceAndCommentsIgnored()
    {
        var compact = TokenizeSource("class A{void M(){return;}}");
        var spaced = TokenizeSource("""
            // header
            class A
            {
                /* block */
                void M() { return; }
            }
            """);
        Assert.Equal(
            compact.Select(t => t.Text).ToList(),
            spaced.Select(t => t.Text).ToList());
    }
}
