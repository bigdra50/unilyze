using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

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
            members.Add(new MemberInfo(param.Identifier.Text, paramType, "Property", [], [], []));
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
                        yield return new MemberInfo(
                            variable.Identifier.Text, fieldType, "Field",
                            fieldModifiers, [], fieldAttrs);
                    break;

                case PropertyDeclarationSyntax prop:
                    yield return new MemberInfo(
                        prop.Identifier.Text, prop.Type.ToString(), "Property",
                        GetModifiers(prop.Modifiers), [], GetAttributeInfos(prop.AttributeLists));
                    break;

                case MethodDeclarationSyntax method:
                    yield return CreateMethodMember(method);
                    break;

                // N2: Event declarations
                case EventFieldDeclarationSyntax eventField:
                    var eventType = eventField.Declaration.Type.ToString();
                    var eventModifiers = GetModifiers(eventField.Modifiers);
                    var eventAttrs = GetAttributeInfos(eventField.AttributeLists);
                    foreach (var variable in eventField.Declaration.Variables)
                        yield return new MemberInfo(
                            variable.Identifier.Text, eventType, "Event",
                            eventModifiers, [], eventAttrs);
                    break;

                case EventDeclarationSyntax eventDecl:
                    yield return new MemberInfo(
                        eventDecl.Identifier.Text, eventDecl.Type.ToString(), "Event",
                        GetModifiers(eventDecl.Modifiers), [],
                        GetAttributeInfos(eventDecl.AttributeLists));
                    break;

                // N3: Indexer declarations
                case IndexerDeclarationSyntax indexer:
                    var indexParams = indexer.ParameterList.Parameters
                        .Select(p => new ParameterInfo(p.Identifier.Text, p.Type?.ToString() ?? "unknown"))
                        .ToList();
                    yield return new MemberInfo(
                        "this[]", indexer.Type.ToString(), "Indexer",
                        GetModifiers(indexer.Modifiers), indexParams,
                        GetAttributeInfos(indexer.AttributeLists));
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
        return new MemberInfo(
            method.Identifier.Text, method.ReturnType.ToString(), "Method",
            GetModifiers(method.Modifiers), methodParams,
            GetAttributeInfos(method.AttributeLists), cogCC, cycCC, nestDepth, methodLineCount, methodStartLine,
            halstead.Volume, halstead.Difficulty, halstead.Effort, halstead.EstimatedBugs);
    }

    internal static IReadOnlyList<string> GetModifiers(SyntaxTokenList modifiers)
        => modifiers.Select(m => m.Text).ToList();

    // N5: Attribute extraction with arguments
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
