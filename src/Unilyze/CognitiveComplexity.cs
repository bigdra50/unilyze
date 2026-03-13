using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public static class CognitiveComplexity
{
    public static int Calculate(SyntaxNode? body)
    {
        if (body == null) return 0;
        var state = new State();
        Walk(body, state, nesting: 0);

        // Direct recursion: +1 if the method calls itself
        var methodName = GetEnclosingMethodName(body);
        if (methodName != null && HasDirectRecursion(body, methodName))
            state.Total += 1;

        return state.Total;
    }

    sealed class State
    {
        public int Total;
        // Track last binary operator kind for sequence detection
        public SyntaxKind LastBinaryOp = SyntaxKind.None;
    }

    static void Walk(SyntaxNode node, State state, int nesting)
    {
        foreach (var child in node.ChildNodes())
        {
            // Reset operator tracking between sibling nodes
            // so independent statements don't share LastBinaryOp state
            state.LastBinaryOp = SyntaxKind.None;

            switch (child)
            {
                case IfStatementSyntax ifStmt:
                    HandleIf(ifStmt, state, nesting);
                    break;

                case SwitchStatementSyntax:
                case SwitchExpressionSyntax:
                    state.Total += 1 + nesting; // structural + nesting
                    Walk(child, state, nesting + 1);
                    break;

                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case WhileStatementSyntax:
                case DoStatementSyntax:
                    state.Total += 1 + nesting;
                    Walk(child, state, nesting + 1);
                    break;

                case CatchClauseSyntax:
                    state.Total += 1 + nesting;
                    Walk(child, state, nesting + 1);
                    break;

                case GotoStatementSyntax:
                    state.Total += 1;
                    Walk(child, state, nesting);
                    break;

                case ConditionalExpressionSyntax:
                    state.Total += 1 + nesting;
                    Walk(child, state, nesting + 1);
                    break;

                case BinaryExpressionSyntax binary:
                    HandleBinaryExpression(binary, state, nesting);
                    break;

                case IsPatternExpressionSyntax isPattern:
                    WalkExpression(isPattern.Expression, state, nesting);
                    WalkPattern(isPattern.Pattern, state, nesting);
                    break;

                case LocalFunctionStatementSyntax localFunc:
                    if (localFunc.Modifiers.Any(SyntaxKind.StaticKeyword))
                    {
                        // static local functions are calculated independently
                        // They don't contribute to the parent method's complexity
                        // (their own complexity is tracked separately)
                        var independentState = new State();
                        Walk(localFunc.Body ?? (SyntaxNode?)localFunc.ExpressionBody ?? child, independentState, nesting: 0);
                    }
                    else
                    {
                        Walk(child, state, nesting);
                    }
                    break;

                // Nesting increasers without structural increment
                case LambdaExpressionSyntax:
                case AnonymousMethodExpressionSyntax:
                    Walk(child, state, nesting + 1);
                    break;

                default:
                    Walk(child, state, nesting);
                    break;
            }
        }
    }

    static void HandleIf(IfStatementSyntax ifStmt, State state, int nesting)
    {
        // First if in a chain: structural + nesting
        state.Total += 1 + nesting;

        // Walk condition and statement (but not Else, we handle it separately)
        if (ifStmt.Condition != null)
            WalkExpression(ifStmt.Condition, state, nesting + 1);
        if (ifStmt.Statement != null)
            Walk(ifStmt.Statement, state, nesting + 1);

        // Handle else / else if chain
        var elseClause = ifStmt.Else;
        while (elseClause != null)
        {
            if (elseClause.Statement is IfStatementSyntax elseIf)
            {
                // else if: +1 structural only (no nesting increment)
                state.LastBinaryOp = SyntaxKind.None;
                state.Total += 1;
                if (elseIf.Condition != null)
                    WalkExpression(elseIf.Condition, state, nesting + 1);
                if (elseIf.Statement != null)
                    Walk(elseIf.Statement, state, nesting + 1);
                elseClause = elseIf.Else;
            }
            else
            {
                // else: +1 structural only
                state.Total += 1;
                Walk(elseClause.Statement!, state, nesting + 1);
                elseClause = null;
            }
        }
    }

    static void WalkExpression(SyntaxNode node, State state, int nesting)
    {
        if (node is BinaryExpressionSyntax binary)
        {
            HandleBinaryExpression(binary, state, nesting);
            return;
        }

        if (node is IsPatternExpressionSyntax isPattern)
        {
            WalkExpression(isPattern.Expression, state, nesting);
            WalkPattern(isPattern.Pattern, state, nesting);
            return;
        }

        foreach (var child in node.ChildNodes())
            WalkExpression(child, state, nesting);
    }

    static void HandleBinaryExpression(BinaryExpressionSyntax binary, State state, int nesting)
    {
        var kind = binary.OperatorToken.Kind();
        if (kind is SyntaxKind.AmpersandAmpersandToken or SyntaxKind.BarBarToken)
        {
            // Increment only when operator kind changes from previous
            if (state.LastBinaryOp != kind)
            {
                state.Total += 1;
                state.LastBinaryOp = kind;
            }

            // Walk left, then right
            WalkLogicalOperand(binary.Left, state, nesting);
            // Restore current operator context so the right subtree
            // sees a kind change if it contains a different operator
            state.LastBinaryOp = kind;
            WalkLogicalOperand(binary.Right, state, nesting);
        }
        else if (binary.IsKind(SyntaxKind.CoalesceExpression))
        {
            // ?? is shorthand — no increment per SonarSource spec
            Walk(binary, state, nesting);
        }
        else
        {
            Walk(binary, state, nesting);
        }
    }

    static void WalkLogicalOperand(ExpressionSyntax operand, State state, int nesting)
    {
        if (operand is BinaryExpressionSyntax inner)
        {
            var innerKind = inner.OperatorToken.Kind();
            if (innerKind is SyntaxKind.AmpersandAmpersandToken or SyntaxKind.BarBarToken)
            {
                HandleBinaryExpression(inner, state, nesting);
                return;
            }
        }

        if (operand is IsPatternExpressionSyntax isPattern)
        {
            WalkExpression(isPattern.Expression, state, nesting);
            WalkPattern(isPattern.Pattern, state, nesting);
            return;
        }

        // Reset operator tracking after non-logical expression
        var saved = state.LastBinaryOp;
        state.LastBinaryOp = SyntaxKind.None;
        Walk(operand, state, nesting);
        state.LastBinaryOp = saved;
    }

    static void WalkPattern(PatternSyntax pattern, State state, int nesting)
    {
        switch (pattern)
        {
            case BinaryPatternSyntax binaryPattern:
                HandlePatternCombinator(binaryPattern, state, nesting);
                break;
            case ParenthesizedPatternSyntax paren:
                WalkPattern(paren.Pattern, state, nesting);
                break;
            case UnaryPatternSyntax unary:
                WalkPattern(unary.Pattern, state, nesting);
                break;
            case RecursivePatternSyntax recursive:
                WalkRecursiveSubpatterns(recursive, state, nesting);
                break;
        }
    }

    static void WalkRecursiveSubpatterns(RecursivePatternSyntax recursive, State state, int nesting)
    {
        foreach (var sub in recursive.PositionalPatternClause?.Subpatterns
                            ?? Enumerable.Empty<SubpatternSyntax>())
            if (sub.Pattern != null)
                WalkPattern(sub.Pattern, state, nesting);
        foreach (var sub in recursive.PropertyPatternClause?.Subpatterns
                            ?? Enumerable.Empty<SubpatternSyntax>())
            if (sub.Pattern != null)
                WalkPattern(sub.Pattern, state, nesting);
    }

    static string? GetEnclosingMethodName(SyntaxNode body)
    {
        var parent = body.Parent;
        return parent switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text,
            LocalFunctionStatementSyntax local => local.Identifier.Text,
            _ => null,
        };
    }

    static bool HasDirectRecursion(SyntaxNode body, string methodName)
    {
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = invocation.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax { Name: IdentifierNameSyntax id }
                    when invocation.Expression is MemberAccessExpressionSyntax ma
                    && ma.Expression is ThisExpressionSyntax => id.Identifier.Text,
                _ => null,
            };
            if (name == methodName) return true;
        }
        return false;
    }

    static void HandlePatternCombinator(BinaryPatternSyntax pattern, State state, int nesting)
    {
        // Map or -> BarBarToken, and -> AmpersandAmpersandToken
        // so they share tracking with || and && respectively
        var mappedKind = pattern.IsKind(SyntaxKind.OrPattern)
            ? SyntaxKind.BarBarToken
            : SyntaxKind.AmpersandAmpersandToken;

        if (state.LastBinaryOp != mappedKind)
        {
            state.Total += 1;
            state.LastBinaryOp = mappedKind;
        }

        // Walk left
        WalkPatternOperand(pattern.Left, state, nesting, mappedKind);
        // Restore context for right subtree
        state.LastBinaryOp = mappedKind;
        // Walk right
        WalkPatternOperand(pattern.Right, state, nesting, mappedKind);
    }

    static void WalkPatternOperand(PatternSyntax pattern, State state, int nesting, SyntaxKind parentKind)
    {
        // Unwrap parenthesized patterns
        while (pattern is ParenthesizedPatternSyntax paren)
            pattern = paren.Pattern;

        if (pattern is BinaryPatternSyntax inner)
        {
            HandlePatternCombinator(inner, state, nesting);
            return;
        }

        var saved = state.LastBinaryOp;
        state.LastBinaryOp = SyntaxKind.None;
        WalkPattern(pattern, state, nesting);
        state.LastBinaryOp = saved;
    }
}
