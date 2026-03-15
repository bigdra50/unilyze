using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public static class CyclomaticComplexity
{
    public static int Calculate(SyntaxNode? body, SemanticModel? model = null)
    {
        if (body is null) return 1;

        var walker = new Walker(model);
        walker.Visit(body);
        return 1 + walker.Count;
    }

    static bool IsBooleanType(ExpressionSyntax expression, SemanticModel model)
    {
        var typeInfo = model.GetTypeInfo(expression);
        return typeInfo.Type?.SpecialType == SpecialType.System_Boolean;
    }

    sealed class Walker(SemanticModel? model) : CSharpSyntaxWalker
    {
        public int Count;

        public override void VisitIfStatement(IfStatementSyntax node) { Count++; base.VisitIfStatement(node); }
        public override void VisitConditionalExpression(ConditionalExpressionSyntax node) { Count++; base.VisitConditionalExpression(node); }
        public override void VisitForStatement(ForStatementSyntax node) { Count++; base.VisitForStatement(node); }
        public override void VisitForEachStatement(ForEachStatementSyntax node) { Count++; base.VisitForEachStatement(node); }
        public override void VisitWhileStatement(WhileStatementSyntax node) { Count++; base.VisitWhileStatement(node); }
        public override void VisitDoStatement(DoStatementSyntax node) { Count++; base.VisitDoStatement(node); }
        public override void VisitCatchClause(CatchClauseSyntax node) { Count++; base.VisitCatchClause(node); }
        public override void VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node) { Count++; base.VisitConditionalAccessExpression(node); }
        public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax node) { Count++; base.VisitSwitchExpressionArm(node); }
        public override void VisitGotoStatement(GotoStatementSyntax node) { Count++; base.VisitGotoStatement(node); }

        public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node) { Count++; base.VisitCaseSwitchLabel(node); }
        public override void VisitCasePatternSwitchLabel(CasePatternSwitchLabelSyntax node) { Count++; base.VisitCasePatternSwitchLabel(node); }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.LogicalAndExpression)
                || node.IsKind(SyntaxKind.LogicalOrExpression)
                || node.IsKind(SyntaxKind.CoalesceExpression))
            {
                Count++;
            }
            else if (model is not null
                     && (node.IsKind(SyntaxKind.BitwiseAndExpression) || node.IsKind(SyntaxKind.BitwiseOrExpression))
                     && IsBooleanType(node.Left, model))
            {
                Count++;
            }

            base.VisitBinaryExpression(node);
        }
    }
}
