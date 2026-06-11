using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public sealed record WeakTemporizationFinding(string MethodName, int Line, string Message);

public static class WeakTemporizationAnalyzer
{
    static readonly HashSet<string> ScannedMethodNames = new(StringComparer.Ordinal)
    {
        "Update", "LateUpdate"
    };

    static readonly HashSet<string> TransformMutationProperties = new(StringComparer.Ordinal)
    {
        "position", "localPosition", "rotation", "localRotation",
        "eulerAngles", "localEulerAngles", "localScale"
    };

    static readonly HashSet<string> TransformMutationMethods = new(StringComparer.Ordinal)
    {
        "Translate", "Rotate", "RotateAround"
    };

    static readonly HashSet<string> TimeTemporizationMembers = new(StringComparer.Ordinal)
    {
        "deltaTime", "smoothDeltaTime", "unscaledDeltaTime", "fixedDeltaTime", "time", "unscaledTime"
    };

    public static IReadOnlyList<WeakTemporizationFinding> Analyze(
        TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        var context = UnityContextClassifier.Classify(typeDecl, model);
        if (!context.IsMonoBehaviour)
            return [];

        var findings = new List<WeakTemporizationFinding>();

        foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            if (!ScannedMethodNames.Contains(method.Identifier.Text))
                continue;

            var body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
            if (body is null)
                continue;

            AnalyzeMethod(method.Identifier.Text, body, model, findings);
        }

        return findings;
    }

    static void AnalyzeMethod(
        string methodName,
        SyntaxNode body,
        SemanticModel? model,
        List<WeakTemporizationFinding> findings)
    {
        var localTemporized = BuildLocalTemporizationMap(body, model);

        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case AssignmentExpressionSyntax assign when
                    assign.IsKind(SyntaxKind.AddAssignmentExpression)
                    || assign.IsKind(SyntaxKind.SubtractAssignmentExpression):
                    TryReportAssignment(assign, methodName, model, localTemporized, findings, incrementalOnly: false);
                    break;

                case AssignmentExpressionSyntax assign when assign.IsKind(SyntaxKind.SimpleAssignmentExpression):
                    TryReportAssignment(assign, methodName, model, localTemporized, findings, incrementalOnly: true);
                    break;

                case InvocationExpressionSyntax invocation:
                    TryReportInvocation(invocation, methodName, model, localTemporized, findings);
                    break;
            }
        }
    }

    static void TryReportAssignment(
        AssignmentExpressionSyntax assign,
        string methodName,
        SemanticModel? model,
        IReadOnlyDictionary<string, bool> localTemporized,
        List<WeakTemporizationFinding> findings,
        bool incrementalOnly)
    {
        if (assign.Left is not MemberAccessExpressionSyntax leftMember)
            return;

        if (!IsTransformMutationMember(leftMember, model))
            return;

        if (incrementalOnly && !ReadsSameTransformProperty(assign.Right, leftMember, model))
            return;

        if (IsTemporized(assign.Right, localTemporized))
            return;

        var line = GetStatementLine(assign);
        findings.Add(new WeakTemporizationFinding(
            methodName,
            line,
            "transform mutation without delta-time scaling"));
    }

    static void TryReportInvocation(
        InvocationExpressionSyntax invocation,
        string methodName,
        SemanticModel? model,
        IReadOnlyDictionary<string, bool> localTemporized,
        List<WeakTemporizationFinding> findings)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (!IsTransformReceiver(memberAccess.Expression, model))
            return;

        if (!TransformMutationMethods.Contains(memberAccess.Name.Identifier.Text))
            return;

        if (invocation.ArgumentList.Arguments.Any(arg => IsTemporized(arg.Expression, localTemporized)))
            return;

        var line = GetStatementLine(invocation);
        findings.Add(new WeakTemporizationFinding(
            methodName,
            line,
            "transform mutation without delta-time scaling"));
    }

    static int GetStatementLine(SyntaxNode node)
    {
        var statement = node.Ancestors().OfType<StatementSyntax>().FirstOrDefault() ?? node;
        return statement.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    }

    static Dictionary<string, bool> BuildLocalTemporizationMap(SyntaxNode body, SemanticModel? model)
    {
        var locals = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var declarator in body.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Initializer?.Value is ExpressionSyntax init)
                locals[declarator.Identifier.Text] = ContainsTimeMemberOrTemporizedLocal(init, locals);
        }

        foreach (var assign in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assign.Left is not IdentifierNameSyntax id)
                continue;

            locals[id.Identifier.Text] = ContainsTimeMemberOrTemporizedLocal(assign.Right, locals);
        }

        return locals;
    }

    static bool IsTemporized(ExpressionSyntax expression, IReadOnlyDictionary<string, bool> localTemporized)
        => ContainsTimeMemberOrTemporizedLocal(expression, localTemporized);

    static bool ContainsTimeMemberOrTemporizedLocal(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, bool> localTemporized)
    {
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            if (node is MemberAccessExpressionSyntax memberAccess && IsTimeMemberAccess(memberAccess))
                return true;

            if (node is IdentifierNameSyntax { Parent: not MemberAccessExpressionSyntax } id
                && localTemporized.TryGetValue(id.Identifier.Text, out var temporized)
                && temporized)
            {
                return true;
            }
        }

        return false;
    }

    static bool IsTimeMemberAccess(MemberAccessExpressionSyntax memberAccess)
    {
        if (!TimeTemporizationMembers.Contains(memberAccess.Name.Identifier.Text))
            return false;

        return memberAccess.Expression switch
        {
            IdentifierNameSyntax { Identifier.Text: "Time" } => true,
            QualifiedNameSyntax qual => qual.Right.Identifier.Text == "Time",
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text == "Time",
            MemberAccessExpressionSyntax { Name.Identifier.Text: "Time" } => true,
            _ => false
        };
    }

    static bool IsTransformMutationMember(MemberAccessExpressionSyntax memberAccess, SemanticModel? model)
    {
        if (!TransformMutationProperties.Contains(memberAccess.Name.Identifier.Text))
            return false;

        return IsTransformReceiver(memberAccess.Expression, model);
    }

    static bool IsTransformReceiver(ExpressionSyntax receiver, SemanticModel? model)
    {
        if (!MatchesTransformReceiverSyntax(receiver))
            return false;

        if (model is null)
            return true;

        var type = model.GetTypeInfo(receiver).Type;
        if (type is null)
            return true;

        if (type.Name != "Transform")
            return false;

        var ns = type.ContainingNamespace?.ToDisplayString();
        return ns is "UnityEngine" or "global::UnityEngine";
    }

    static bool MatchesTransformReceiverSyntax(ExpressionSyntax receiver)
    {
        return receiver switch
        {
            IdentifierNameSyntax { Identifier.Text: "transform" } => true,
            MemberAccessExpressionSyntax
            {
                Expression: ThisExpressionSyntax,
                Name.Identifier.Text: "transform"
            } => true,
            _ => false
        };
    }

    static bool ReadsSameTransformProperty(
        ExpressionSyntax rhs,
        MemberAccessExpressionSyntax lhsMember,
        SemanticModel? model)
    {
        var propertyName = lhsMember.Name.Identifier.Text;

        foreach (var memberAccess in rhs.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            if (memberAccess.Name.Identifier.Text != propertyName)
                continue;

            if (!IsTransformReceiver(memberAccess.Expression, model))
                continue;

            return true;
        }

        return false;
    }
}

public sealed class WeakTemporizationSmellDetector : ISmellDetector
{
    public IReadOnlyList<DetectedSmell> Detect(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        var typeName = SmellDetectorHelpers.GetTypeName(typeDecl);
        return WeakTemporizationAnalyzer.Analyze(typeDecl, model)
            .Select(f => new DetectedSmell(
                CodeSmellKind.WeakTemporization,
                SmellSeverity.Warning,
                typeName,
                f.MethodName,
                f.Message,
                f.Line))
            .ToList();
    }
}
