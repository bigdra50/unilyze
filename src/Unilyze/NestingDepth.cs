using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public static class NestingDepth
{
    public static int Calculate(SyntaxNode? body)
    {
        if (body is null) return 0;

        var maxDepth = 0;
        Walk(body, 0, ref maxDepth);
        return maxDepth;
    }

    static void Walk(SyntaxNode node, int depth, ref int maxDepth)
    {
        foreach (var child in node.ChildNodes())
        {
            if (IsNestingNode(child))
            {
                var newDepth = depth + 1;
                if (newDepth > maxDepth) maxDepth = newDepth;
                Walk(child, newDepth, ref maxDepth);
            }
            else
            {
                Walk(child, depth, ref maxDepth);
            }
        }
    }

    static bool IsNestingNode(SyntaxNode node) => node is
        IfStatementSyntax or
        ElseClauseSyntax or
        ForStatementSyntax or
        ForEachStatementSyntax or
        WhileStatementSyntax or
        DoStatementSyntax or
        SwitchStatementSyntax or
        SwitchExpressionSyntax or
        CatchClauseSyntax or
        LambdaExpressionSyntax or
        AnonymousMethodExpressionSyntax or
        ConditionalExpressionSyntax;
}
