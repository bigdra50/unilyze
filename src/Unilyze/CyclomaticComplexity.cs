using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public static class CyclomaticComplexity
{
    public static int Calculate(SyntaxNode? body, SemanticModel? model = null)
    {
        if (body is null) return 1;

        var count = 1; // base path

        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case IfStatementSyntax:
                case ConditionalExpressionSyntax:
                case CaseSwitchLabelSyntax:
                case CasePatternSwitchLabelSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case WhileStatementSyntax:
                case DoStatementSyntax:
                case CatchClauseSyntax:
                case ConditionalAccessExpressionSyntax:
                case SwitchExpressionArmSyntax:
                    count++;
                    break;

                case BinaryExpressionSyntax binary
                    when binary.IsKind(SyntaxKind.LogicalAndExpression)
                      || binary.IsKind(SyntaxKind.LogicalOrExpression)
                      || binary.IsKind(SyntaxKind.CoalesceExpression):
                    count++;
                    break;

                case BinaryExpressionSyntax binary2
                    when model is not null
                      && (binary2.IsKind(SyntaxKind.BitwiseAndExpression)
                       || binary2.IsKind(SyntaxKind.BitwiseOrExpression))
                      && IsBooleanType(binary2.Left, model):
                    count++;
                    break;

                case GotoStatementSyntax:
                    count++;
                    break;
            }
        }

        return count;
    }

    static bool IsBooleanType(ExpressionSyntax expression, SemanticModel model)
    {
        var typeInfo = model.GetTypeInfo(expression);
        return typeInfo.Type?.SpecialType == SpecialType.System_Boolean;
    }
}
