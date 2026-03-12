using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnityRoslynGraph;

public static class CognitiveComplexity
{
    public static int Calculate(SyntaxNode? body)
    {
        if (body == null) return 0;
        var state = new State();
        Walk(body, state, nesting: 0);
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
        // Walk expression tree looking for binary logical operators
        foreach (var child in node.ChildNodes())
        {
            if (child is BinaryExpressionSyntax binary)
                HandleBinaryExpression(binary, state, nesting);
            else
                WalkExpression(child, state, nesting);
        }
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
            WalkLogicalOperand(binary.Right, state, nesting);
        }
        else if (binary.IsKind(SyntaxKind.CoalesceExpression))
        {
            state.Total += 1; // ?? operator
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

        // Reset operator tracking after non-logical expression
        var saved = state.LastBinaryOp;
        state.LastBinaryOp = SyntaxKind.None;
        Walk(operand, state, nesting);
        state.LastBinaryOp = saved;
    }
}
