using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze;

internal static class CloneTokenizer
{
    public static IReadOnlyList<FileTokenSequence> Tokenize(IReadOnlyList<SyntaxTree> trees)
    {
        var sequences = new List<FileTokenSequence>(trees.Count);
        foreach (var tree in trees)
        {
            var filePath = tree.FilePath;
            if (string.IsNullOrEmpty(filePath))
                continue;

            var tokens = ExtractNormalizedTokens(tree);
            if (tokens.Count == 0)
                continue;

            var lineCount = File.Exists(filePath)
                ? File.ReadAllLines(filePath).Length
                : tokens[^1].EndLine;

            sequences.Add(new FileTokenSequence(filePath, tokens, lineCount));
        }

        return sequences;
    }

    static List<NormalizedToken> ExtractNormalizedTokens(SyntaxTree tree)
    {
        var tokens = new List<NormalizedToken>();
        var root = tree.GetRoot();
        foreach (var token in root.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.EndOfFileToken))
                continue;

            var normalized = Normalize(token);
            var span = token.GetLocation().GetLineSpan();
            tokens.Add(new NormalizedToken(
                normalized,
                span.StartLinePosition.Line + 1,
                span.EndLinePosition.Line + 1));
        }

        return tokens;
    }

    static string Normalize(SyntaxToken token)
    {
        if (IsLiteralToken(token))
            return "LIT";

        if (IsIdentifierToken(token))
            return "ID";

        return token.Text;
    }

    static bool IsLiteralToken(SyntaxToken token) =>
        token.IsKind(SyntaxKind.NumericLiteralToken)
        || token.IsKind(SyntaxKind.StringLiteralToken)
        || token.IsKind(SyntaxKind.CharacterLiteralToken)
        || token.IsKind(SyntaxKind.TrueKeyword)
        || token.IsKind(SyntaxKind.FalseKeyword)
        || token.IsKind(SyntaxKind.InterpolatedStringTextToken);

    static bool IsIdentifierToken(SyntaxToken token) =>
        token.IsKind(SyntaxKind.IdentifierToken);
}
