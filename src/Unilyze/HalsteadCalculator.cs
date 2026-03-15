using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public sealed record HalsteadMetrics(
    int UniqueOperators,
    int UniqueOperands,
    int TotalOperators,
    int TotalOperands,
    double Volume);

public static class HalsteadCalculator
{
    static readonly ConcurrentQueue<Dictionary<string, int>> DictPool = new();

    /// <summary>
    /// Pre-computed SyntaxKind -> string map for keyword-operator nodes.
    /// Eliminates per-call <c>Kind().ToString()</c> allocations.
    /// </summary>
    static readonly FrozenDictionary<SyntaxKind, string> KeywordOperatorNames =
        new Dictionary<SyntaxKind, string>
        {
            [SyntaxKind.IfStatement] = "IfStatement",
            [SyntaxKind.ElseClause] = "ElseClause",
            [SyntaxKind.ForStatement] = "ForStatement",
            [SyntaxKind.ForEachStatement] = "ForEachStatement",
            [SyntaxKind.WhileStatement] = "WhileStatement",
            [SyntaxKind.DoStatement] = "DoStatement",
            [SyntaxKind.SwitchStatement] = "SwitchStatement",
            [SyntaxKind.SwitchExpression] = "SwitchExpression",
            [SyntaxKind.CaseSwitchLabel] = "CaseSwitchLabel",
            [SyntaxKind.CasePatternSwitchLabel] = "CasePatternSwitchLabel",
            [SyntaxKind.SwitchExpressionArm] = "SwitchExpressionArm",
            [SyntaxKind.ReturnStatement] = "ReturnStatement",
            [SyntaxKind.ThrowStatement] = "ThrowStatement",
            [SyntaxKind.ThrowExpression] = "ThrowExpression",
            [SyntaxKind.TryStatement] = "TryStatement",
            [SyntaxKind.CatchClause] = "CatchClause",
            [SyntaxKind.FinallyClause] = "FinallyClause",
            [SyntaxKind.ObjectCreationExpression] = "ObjectCreationExpression",
            [SyntaxKind.ImplicitObjectCreationExpression] = "ImplicitObjectCreationExpression",
            [SyntaxKind.TypeOfExpression] = "TypeOfExpression",
            [SyntaxKind.IsPatternExpression] = "IsPatternExpression",
            [SyntaxKind.AsExpression] = "AsExpression",
            [SyntaxKind.AwaitExpression] = "AwaitExpression",
            [SyntaxKind.YieldReturnStatement] = "YieldReturnStatement",
            [SyntaxKind.YieldBreakStatement] = "YieldBreakStatement",
            [SyntaxKind.ConditionalAccessExpression] = "ConditionalAccessExpression",
        }.ToFrozenDictionary();

    public static HalsteadMetrics Calculate(SyntaxNode? body)
    {
        if (body is null)
            return new HalsteadMetrics(0, 0, 0, 0, 0);

        var operatorCounts = RentDict();
        var operandCounts = RentDict();

        try
        {
            var walker = new HalsteadWalker(operatorCounts, operandCounts);
            walker.Visit(body);

            var n1 = SumValues(operatorCounts);
            var n2 = SumValues(operandCounts);
            var eta1 = operatorCounts.Count;
            var eta2 = operandCounts.Count;

            var programLength = n1 + n2;
            var vocabulary = eta1 + eta2;

            var volume = vocabulary > 0
                ? programLength * Math.Log2(vocabulary)
                : 0.0;

            return new HalsteadMetrics(eta1, eta2, n1, n2, volume);
        }
        finally
        {
            ReturnDict(operatorCounts);
            ReturnDict(operandCounts);
        }
    }

    public static double ComputeMaintainabilityIndex(
        double halsteadVolume, int cyclomaticComplexity, int lineCount)
    {
        var loc = Math.Max(1, lineCount);

        if (halsteadVolume <= 0)
            return 100.0;

        var raw = 171.0
                  - 5.2 * Math.Log(halsteadVolume)
                  - 0.23 * cyclomaticComplexity
                  - 16.2 * Math.Log(loc);

        return Math.Max(0, raw * 100.0 / 171.0);
    }

    static Dictionary<string, int> RentDict()
    {
        if (DictPool.TryDequeue(out var dict))
        {
            dict.Clear();
            return dict;
        }
        return new Dictionary<string, int>();
    }

    static void ReturnDict(Dictionary<string, int> dict)
    {
        if (dict.Count < 1024)
            DictPool.Enqueue(dict);
    }

    static int SumValues(Dictionary<string, int> dict)
    {
        var sum = 0;
        foreach (var value in dict.Values)
            sum += value;
        return sum;
    }

    static void AddToken(Dictionary<string, int> dict, string token)
    {
        ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, token, out _);
        count++;
    }

    /// <summary>
    /// Single-pass walker at token depth.
    /// <see cref="DefaultVisit"/> classifies keyword-operator nodes;
    /// <see cref="VisitToken"/> classifies operator/operand tokens.
    /// </summary>
    sealed class HalsteadWalker(
        Dictionary<string, int> operators,
        Dictionary<string, int> operands)
        : CSharpSyntaxWalker(SyntaxWalkerDepth.Token)
    {
        public override void DefaultVisit(SyntaxNode node)
        {
            if (KeywordOperatorNames.TryGetValue(node.Kind(), out var name))
                AddToken(operators, name);

            base.DefaultVisit(node);
        }

        public override void VisitToken(SyntaxToken token)
        {
            if (IsOperatorToken(token))
                AddToken(operators, token.Text);
            else if (IsOperandToken(token))
                AddToken(operands, token.Text);

            base.VisitToken(token);
        }
    }

    static bool IsOperatorToken(SyntaxToken token)
    {
        return token.Kind() switch
        {
            // Arithmetic
            SyntaxKind.PlusToken or
            SyntaxKind.MinusToken or
            SyntaxKind.AsteriskToken or
            SyntaxKind.SlashToken or
            SyntaxKind.PercentToken => true,

            // Assignment
            SyntaxKind.EqualsToken or
            SyntaxKind.PlusEqualsToken or
            SyntaxKind.MinusEqualsToken or
            SyntaxKind.AsteriskEqualsToken or
            SyntaxKind.SlashEqualsToken or
            SyntaxKind.PercentEqualsToken or
            SyntaxKind.AmpersandEqualsToken or
            SyntaxKind.BarEqualsToken or
            SyntaxKind.CaretEqualsToken or
            SyntaxKind.LessThanLessThanEqualsToken or
            SyntaxKind.GreaterThanGreaterThanEqualsToken => true,

            // Comparison
            SyntaxKind.EqualsEqualsToken or
            SyntaxKind.ExclamationEqualsToken or
            SyntaxKind.LessThanToken or
            SyntaxKind.GreaterThanToken or
            SyntaxKind.LessThanEqualsToken or
            SyntaxKind.GreaterThanEqualsToken => true,

            // Logical
            SyntaxKind.AmpersandAmpersandToken or
            SyntaxKind.BarBarToken or
            SyntaxKind.ExclamationToken => true,

            // Bitwise
            SyntaxKind.AmpersandToken or
            SyntaxKind.BarToken or
            SyntaxKind.CaretToken or
            SyntaxKind.TildeToken or
            SyntaxKind.LessThanLessThanToken or
            SyntaxKind.GreaterThanGreaterThanToken => true,

            // Increment/Decrement
            SyntaxKind.PlusPlusToken or
            SyntaxKind.MinusMinusToken => true,

            // Access
            SyntaxKind.DotToken => true,

            // Null coalescing
            SyntaxKind.QuestionQuestionToken or
            SyntaxKind.QuestionQuestionEqualsToken => true,

            // Lambda
            SyntaxKind.EqualsGreaterThanToken => true,

            _ => false
        };
    }

    static bool IsOperandToken(SyntaxToken token)
    {
        return token.Kind() switch
        {
            // Identifiers
            SyntaxKind.IdentifierToken => !IsKeywordLikeIdentifier(token),

            // Literals
            SyntaxKind.NumericLiteralToken or
            SyntaxKind.StringLiteralToken or
            SyntaxKind.CharacterLiteralToken or
            SyntaxKind.TrueKeyword or
            SyntaxKind.FalseKeyword or
            SyntaxKind.NullKeyword => true,

            // this / base
            SyntaxKind.ThisKeyword or
            SyntaxKind.BaseKeyword => true,

            _ => false
        };
    }

    static bool IsKeywordLikeIdentifier(SyntaxToken token)
    {
        var parent = token.Parent;
        return parent is TypeSyntax
            or BaseTypeSyntax
            or TypeParameterSyntax
            or ParameterSyntax
            or TypeParameterConstraintSyntax;
    }
}
