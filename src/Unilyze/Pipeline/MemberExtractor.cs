using Unilyze.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Pipeline;

internal static class MemberExtractor
{
    internal static void AddRecordParameters(TypeDeclarationSyntax typeDecl, List<MemberInfo> members, List<string> ctorParams)
    {
        if (typeDecl is not RecordDeclarationSyntax { ParameterList: { } paramList })
            return;

        foreach (var param in paramList.Parameters)
        {
            var paramType = param.Type?.ToString() ?? "unknown";
            ctorParams.Add(paramType);
            var location = ComputeLocation(param);
            members.Add(new MemberInfo(param.Identifier.Text, paramType, "Property", [], [], [],
                Location: location));
        }
    }

    internal static IEnumerable<MemberInfo> ExtractMembers(TypeDeclarationSyntax typeDecl)
    {
        foreach (var member in typeDecl.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    var fieldType = field.Declaration.Type.ToString();
                    var fieldModifiers = GetModifiers(field.Modifiers);
                    var fieldAttrs = GetAttributeInfos(field.AttributeLists);
                    foreach (var variable in field.Declaration.Variables)
                    {
                        var location = ComputeLocation(variable);
                        yield return new MemberInfo(
                            variable.Identifier.Text, fieldType, "Field",
                            fieldModifiers, [], fieldAttrs, Location: location);
                    }
                    break;

                case PropertyDeclarationSyntax prop:
                    var propLocation = ComputeLocation(prop);
                    yield return new MemberInfo(
                        prop.Identifier.Text, prop.Type.ToString(), "Property",
                        GetModifiers(prop.Modifiers), [], GetAttributeInfos(prop.AttributeLists),
                        Location: propLocation);
                    break;

                case MethodDeclarationSyntax method:
                    yield return CreateMethodMember(method);
                    break;

                case ConstructorDeclarationSyntax ctor:
                    yield return CreateConstructorMember(ctor);
                    break;

                case DestructorDeclarationSyntax dtor:
                    yield return CreateDestructorMember(dtor);
                    break;

                case OperatorDeclarationSyntax op:
                    yield return CreateOperatorMember(op);
                    break;

                case ConversionOperatorDeclarationSyntax conv:
                    yield return CreateConversionOperatorMember(conv);
                    break;

                case EventFieldDeclarationSyntax eventField:
                    var eventType = eventField.Declaration.Type.ToString();
                    var eventModifiers = GetModifiers(eventField.Modifiers);
                    var eventAttrs = GetAttributeInfos(eventField.AttributeLists);
                    foreach (var variable in eventField.Declaration.Variables)
                    {
                        var location = ComputeLocation(variable);
                        yield return new MemberInfo(
                            variable.Identifier.Text, eventType, "Event",
                            eventModifiers, [], eventAttrs, Location: location);
                    }
                    break;

                case EventDeclarationSyntax eventDecl:
                    var evtLocation = ComputeLocation(eventDecl);
                    yield return new MemberInfo(
                        eventDecl.Identifier.Text, eventDecl.Type.ToString(), "Event",
                        GetModifiers(eventDecl.Modifiers), [],
                        GetAttributeInfos(eventDecl.AttributeLists),
                        Location: evtLocation);
                    break;

                case IndexerDeclarationSyntax indexer:
                    var indexParams = indexer.ParameterList.Parameters
                        .Select(p => new ParameterInfo(p.Identifier.Text, p.Type?.ToString() ?? "unknown"))
                        .ToList();
                    var idxLocation = ComputeLocation(indexer);
                    yield return new MemberInfo(
                        "this[]", indexer.Type.ToString(), "Indexer",
                        GetModifiers(indexer.Modifiers), indexParams,
                        GetAttributeInfos(indexer.AttributeLists),
                        Location: idxLocation);
                    break;
            }
        }
    }

    internal static IEnumerable<string> ExtractConstructorParams(TypeDeclarationSyntax typeDecl)
    {
        foreach (var ctor in typeDecl.Members.OfType<ConstructorDeclarationSyntax>())
        {
            foreach (var param in ctor.ParameterList.Parameters)
                yield return param.Type?.ToString() ?? "unknown";
        }
    }

    internal static IReadOnlyList<GenericConstraintInfo> ExtractGenericConstraints(TypeDeclarationSyntax typeDecl)
    {
        if (typeDecl.ConstraintClauses.Count == 0) return [];
        return typeDecl.ConstraintClauses
            .Select(cc => new GenericConstraintInfo(
                cc.Name.ToString(), cc.Constraints.Select(c => c.ToString()).ToList()))
            .ToList();
    }

    internal static MemberInfo CreateMethodMember(MethodDeclarationSyntax method)
    {
        var methodParams = method.ParameterList.Parameters
            .Select(p => new ParameterInfo(p.Identifier.Text, p.Type?.ToString() ?? "unknown"))
            .ToList();
        var bodyNode = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        var (cogCC, cycCC, nestDepth, halstead) = MethodMetricsCalculator.Calculate(bodyNode);
        var methodSpan = method.GetLocation().GetLineSpan();
        var methodLineCount = methodSpan.EndLinePosition.Line - methodSpan.StartLinePosition.Line + 1;
        var methodStartLine = methodSpan.StartLinePosition.Line + 1;
        var location = ComputeLocation(method);
        return new MemberInfo(
            method.Identifier.Text, method.ReturnType.ToString(), "Method",
            GetModifiers(method.Modifiers), methodParams,
            GetAttributeInfos(method.AttributeLists), cogCC, cycCC, nestDepth, methodLineCount, methodStartLine,
            halstead.Volume, halstead.Difficulty, halstead.Effort, halstead.EstimatedBugs,
            location);
    }

    static MemberInfo CreateConstructorMember(ConstructorDeclarationSyntax ctor)
    {
        var ctorParams = ctor.ParameterList.Parameters
            .Select(p => new ParameterInfo(p.Identifier.Text, p.Type?.ToString() ?? "unknown"))
            .ToList();
        var bodyNode = (SyntaxNode?)ctor.Body ?? ctor.ExpressionBody;
        var (cogCC, cycCC, nestDepth, halstead) = MethodMetricsCalculator.Calculate(bodyNode);
        var span = ctor.GetLocation().GetLineSpan();
        var lineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
        var startLine = span.StartLinePosition.Line + 1;
        var location = ComputeLocation(ctor);
        return new MemberInfo(
            ctor.Identifier.Text + ".ctor", "void", "Constructor",
            GetModifiers(ctor.Modifiers), ctorParams,
            GetAttributeInfos(ctor.AttributeLists), cogCC, cycCC, nestDepth, lineCount, startLine,
            halstead.Volume, halstead.Difficulty, halstead.Effort, halstead.EstimatedBugs,
            location);
    }

    static MemberInfo CreateDestructorMember(DestructorDeclarationSyntax dtor)
    {
        var bodyNode = (SyntaxNode?)dtor.Body ?? dtor.ExpressionBody;
        var (cogCC, cycCC, nestDepth, halstead) = MethodMetricsCalculator.Calculate(bodyNode);
        var span = dtor.GetLocation().GetLineSpan();
        var lineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
        var startLine = span.StartLinePosition.Line + 1;
        var location = ComputeLocation(dtor);
        return new MemberInfo(
            "~" + dtor.Identifier.Text, "void", "Destructor",
            GetModifiers(dtor.Modifiers), [],
            GetAttributeInfos(dtor.AttributeLists), cogCC, cycCC, nestDepth, lineCount, startLine,
            halstead.Volume, halstead.Difficulty, halstead.Effort, halstead.EstimatedBugs,
            location);
    }

    static MemberInfo CreateOperatorMember(OperatorDeclarationSyntax op)
    {
        var opParams = op.ParameterList.Parameters
            .Select(p => new ParameterInfo(p.Identifier.Text, p.Type?.ToString() ?? "unknown"))
            .ToList();
        var bodyNode = (SyntaxNode?)op.Body ?? op.ExpressionBody;
        var (cogCC, cycCC, nestDepth, halstead) = MethodMetricsCalculator.Calculate(bodyNode);
        var span = op.GetLocation().GetLineSpan();
        var lineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
        var startLine = span.StartLinePosition.Line + 1;
        var name = MemberBodyEnumerator.GetOperatorMemberName(op);
        var location = ComputeLocation(op);
        return new MemberInfo(
            name, op.ReturnType.ToString(), "Operator",
            GetModifiers(op.Modifiers), opParams,
            GetAttributeInfos(op.AttributeLists), cogCC, cycCC, nestDepth, lineCount, startLine,
            halstead.Volume, halstead.Difficulty, halstead.Effort, halstead.EstimatedBugs,
            location);
    }

    static MemberInfo CreateConversionOperatorMember(ConversionOperatorDeclarationSyntax conv)
    {
        var convParams = conv.ParameterList.Parameters
            .Select(p => new ParameterInfo(p.Identifier.Text, p.Type?.ToString() ?? "unknown"))
            .ToList();
        var bodyNode = (SyntaxNode?)conv.Body ?? conv.ExpressionBody;
        var (cogCC, cycCC, nestDepth, halstead) = MethodMetricsCalculator.Calculate(bodyNode);
        var span = conv.GetLocation().GetLineSpan();
        var lineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
        var startLine = span.StartLinePosition.Line + 1;
        var name = conv.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ImplicitKeyword)
            ? "op_Implicit"
            : "op_Explicit";
        var location = ComputeLocation(conv);
        return new MemberInfo(
            name, conv.Type.ToString(), "ConversionOperator",
            GetModifiers(conv.Modifiers), convParams,
            GetAttributeInfos(conv.AttributeLists), cogCC, cycCC, nestDepth, lineCount, startLine,
            halstead.Volume, halstead.Difficulty, halstead.Effort, halstead.EstimatedBugs,
            location);
    }

    internal static SourceLocation? ComputeLocation(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        if (!span.IsValid)
            return null;
        var startLine = span.StartLinePosition.Line + 1;
        var endLine = span.EndLinePosition.Line + 1;
        return new SourceLocation(FileRef: 0, StartLine: startLine, EndLine: endLine);
    }

    internal static IReadOnlyList<string> GetModifiers(SyntaxTokenList modifiers)
        => modifiers.Select(m => m.Text).ToList();

    internal static IReadOnlyList<AttributeInfo> GetAttributeInfos(SyntaxList<AttributeListSyntax> attributeLists)
    {
        return attributeLists.SelectMany(al => al.Attributes).Select(a =>
        {
            Dictionary<string, string>? args = null;
            if (a.ArgumentList is { Arguments.Count: > 0 })
            {
                args = new Dictionary<string, string>();
                foreach (var arg in a.ArgumentList.Arguments)
                {
                    var key = arg.NameEquals?.Name.ToString()
                           ?? arg.NameColon?.Name.ToString()
                           ?? $"arg{args.Count}";
                    args[key] = arg.Expression.ToString();
                }
            }

            return new AttributeInfo(a.Name.ToString(), args);
        }).ToList();
    }
}
