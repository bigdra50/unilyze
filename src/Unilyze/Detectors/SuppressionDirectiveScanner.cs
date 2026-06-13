using Unilyze.Output;
using Unilyze.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Detectors;

internal sealed record SuppressionEntry(
    HashSet<CodeSmellKind>? Kinds,
    string? Justification);

internal sealed class SuppressionIndex
{
    static readonly SuppressionIndex EmptyInstance = new([], []);

    readonly Dictionary<int, SuppressionEntry> _nextLineByTargetLine;
    readonly List<DeclarationScope> _declarationScopes;

    sealed record DeclarationScope(
        int StartLine,
        int EndLine,
        string? MethodName,
        SuppressionEntry Entry);

    SuppressionIndex(
        Dictionary<int, SuppressionEntry> nextLineByTargetLine,
        List<DeclarationScope> declarationScopes)
    {
        _nextLineByTargetLine = nextLineByTargetLine;
        _declarationScopes = declarationScopes;
    }

    public static SuppressionIndex Empty => EmptyInstance;

    public static SuppressionIndex Build(TypeDeclarationSyntax typeDecl)
    {
        var nextLine = new Dictionary<int, SuppressionEntry>();
        var scopes = new List<DeclarationScope>();

        AddDeclarationScopeDirectives(typeDecl, methodName: null, scopes);
        foreach (var member in typeDecl.Members)
        {
            if (member is MethodDeclarationSyntax method)
            {
                AddDeclarationScopeDirectives(method, method.Identifier.Text, scopes);
            }
        }

        foreach (var trivia in typeDecl.DescendantTrivia(descendIntoTrivia: true))
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
                continue;

            if (!TryParseDirective(trivia.ToString(), out var isNextLine, out var entry))
                continue;

            if (!isNextLine)
                continue;

            var commentLine = trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var targetLine = commentLine + 1;
            nextLine[targetLine] = MergeEntries(nextLine.GetValueOrDefault(targetLine), entry);
        }

        return new SuppressionIndex(nextLine, scopes);
    }

    public bool IsDetectorSmellSuppressed(DetectedSmell smell, out string? justification)
    {
        if (TryMatchNextLine(smell.Kind, smell.Line, out justification))
            return true;

        if (smell.Line is > 0)
        {
            if (TryMatchDeclarationScope(smell.Kind, smell.Line.Value, smell.MethodName, out justification))
                return true;
        }

        justification = null;
        return false;
    }

    public bool IsMetricSmellSuppressed(CodeSmell smell, out string? justification)
    {
        if (IsTypeScopedMetricKind(smell.Kind))
            return TryMatchDeclarationScope(smell.Kind, line: null, methodName: null, out justification);

        if (smell.MethodName is null)
        {
            justification = null;
            return false;
        }

        return TryMatchDeclarationScope(smell.Kind, line: null, smell.MethodName, out justification);
    }

    static void AddDeclarationScopeDirectives(
        SyntaxNode declaration,
        string? methodName,
        List<DeclarationScope> scopes)
    {
        foreach (var trivia in declaration.GetLeadingTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
                continue;

            if (!TryParseDirective(trivia.ToString(), out var isNextLine, out var entry) || isNextLine)
                continue;

            var span = declaration.GetLocation().GetLineSpan();
            scopes.Add(new DeclarationScope(
                span.StartLinePosition.Line + 1,
                span.EndLinePosition.Line + 1,
                methodName,
                entry));
        }
    }

    bool TryMatchNextLine(CodeSmellKind kind, int? line, out string? justification)
    {
        justification = null;
        if (line is not > 0)
            return false;

        if (!_nextLineByTargetLine.TryGetValue(line.Value, out var entry))
            return false;

        if (!EntryMatchesKind(entry, kind))
            return false;

        justification = entry.Justification;
        return true;
    }

    bool TryMatchDeclarationScope(
        CodeSmellKind kind,
        int? line,
        string? methodName,
        out string? justification)
    {
        justification = null;
        foreach (var scope in _declarationScopes)
        {
            if (IsTypeScopedMetricKind(kind))
            {
                if (scope.MethodName is not null)
                    continue;
            }
            else if (methodName is not null)
            {
                if (scope.MethodName is null
                    || !string.Equals(scope.MethodName, methodName, StringComparison.Ordinal))
                    continue;
            }
            else if (scope.MethodName is not null)
            {
                continue;
            }

            if (line is > 0)
            {
                var targetLine = line.Value;
                if (targetLine < scope.StartLine || targetLine > scope.EndLine)
                    continue;
            }

            if (!EntryMatchesKind(scope.Entry, kind))
                continue;

            justification = scope.Entry.Justification;
            return true;
        }

        return false;
    }

    static bool EntryMatchesKind(SuppressionEntry entry, CodeSmellKind kind)
        => entry.Kinds is null || entry.Kinds.Contains(kind);

    static SuppressionEntry MergeEntries(SuppressionEntry? existing, SuppressionEntry incoming)
    {
        if (existing is null)
            return incoming;

        if (existing.Kinds is null || incoming.Kinds is null)
            return incoming with { Kinds = null };

        existing.Kinds.UnionWith(incoming.Kinds);
        return existing with
        {
            Justification = incoming.Justification ?? existing.Justification
        };
    }

    static bool IsTypeScopedMetricKind(CodeSmellKind kind)
        => kind is CodeSmellKind.GodClass
            or CodeSmellKind.LowCohesion
            or CodeSmellKind.HighCoupling
            or CodeSmellKind.DeepInheritance;

    internal static bool TryParseDirective(
        string commentText,
        out bool isNextLine,
        out SuppressionEntry entry)
    {
        isNextLine = false;
        entry = new SuppressionEntry(null, null);

        var text = commentText.TrimStart('/', ' ').Trim();
        if (!text.StartsWith("unilyze-disable", StringComparison.Ordinal))
            return false;

        var rest = text["unilyze-disable".Length..];
        if (rest.StartsWith("-next-line", StringComparison.Ordinal))
        {
            isNextLine = true;
            rest = rest["-next-line".Length..];
        }

        rest = rest.TrimStart();
        string? justification = null;
        var dashIndex = rest.IndexOf("--", StringComparison.Ordinal);
        if (dashIndex >= 0)
        {
            justification = rest[(dashIndex + 2)..].Trim();
            rest = rest[..dashIndex].Trim();
        }

        HashSet<CodeSmellKind>? kinds = null;
        if (rest.Length > 0)
        {
            kinds = new HashSet<CodeSmellKind>();
            foreach (var token in rest.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(token, "UNI009", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine(
                        "Warning: UNI009 (CyclicDependency) cannot be suppressed inline; ignoring.");
                    continue;
                }

                if (SarifFormatter.TryGetKind(token, out var kind))
                {
                    kinds.Add(kind);
                    continue;
                }

                Console.Error.WriteLine($"Warning: Unknown rule id '{token}' in unilyze-disable comment; ignoring.");
            }

            if (kinds.Count == 0)
                kinds = null;
        }

        entry = new SuppressionEntry(kinds, justification);
        return true;
    }
}

internal static class InlineSuppression
{
    public static int CountSuppressed(IReadOnlyList<TypeMetrics> typeMetrics)
        => typeMetrics.Sum(t => t.CodeSmells?.Count(s => s.Suppressed == true) ?? 0);

    public static void WriteSummary(int inlineSuppressedCount)
    {
        if (inlineSuppressedCount <= 0)
            return;

        Console.Error.WriteLine(
            $"{inlineSuppressedCount} smell(s) suppressed by inline comments.");
    }
}
