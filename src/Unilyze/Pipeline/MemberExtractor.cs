using Unilyze.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Pipeline;

internal static class MemberExtractor
{
    internal static void AddRecordParameters(TypeDeclarationSyntax typeDecl, List<MemberInfo> members, List<string> ctorParams, string? typeId = null)
    {
        if (typeDecl is not RecordDeclarationSyntax { ParameterList: { } paramList })
            return;

        foreach (var param in paramList.Parameters)
        {
            var paramType = param.Type?.ToString() ?? "unknown";
            ctorParams.Add(paramType);
            var location = ComputeLocation(param);
            var memberId = typeId is not null
                ? MemberIdentity.CreatePropertyId(typeId, param.Identifier.Text)
                : null;
            members.Add(new MemberInfo(param.Identifier.Text, paramType, "Property", [], [], [],
                Location: location, MemberId: memberId));
        }
    }

    internal static IEnumerable<MemberInfo> ExtractMembers(TypeDeclarationSyntax typeDecl)
        => ExtractMembers(typeDecl, null);

    internal static IEnumerable<MemberInfo> ExtractMembers(TypeDeclarationSyntax typeDecl, string? typeId)
    {
        foreach (var member in typeDecl.Members)
        {
            foreach (var extracted in ExtractSingleMember(member, typeId))
                yield return extracted;
        }
    }

    static IEnumerable<MemberInfo> ExtractSingleMember(MemberDeclarationSyntax member, string? typeId) => member switch
    {
        FieldDeclarationSyntax field => ExtractFieldMembers(field, typeId),
        PropertyDeclarationSyntax prop => [CreatePropertyMember(prop, typeId)],
        MethodDeclarationSyntax method => [CreateMethodMember(method, typeId)],
        ConstructorDeclarationSyntax ctor => [CreateExecutableMember(ctor, ctor.Identifier.Text + ".ctor", "void", "Constructor", ctor.ParameterList, ctor.Modifiers, ctor.AttributeLists, typeId is not null ? MemberIdentity.CreateConstructorId(typeId, ctor) : null)],
        DestructorDeclarationSyntax dtor => [CreateExecutableMember(dtor, "~" + dtor.Identifier.Text, "void", "Destructor", null, dtor.Modifiers, dtor.AttributeLists, typeId is not null ? MemberIdentity.CreateDestructorId(typeId) : null)],
        OperatorDeclarationSyntax op => [CreateExecutableMember(op, MemberBodyEnumerator.GetOperatorMemberName(op), op.ReturnType.ToString(), "Operator", op.ParameterList, op.Modifiers, op.AttributeLists, typeId is not null ? MemberIdentity.CreateOperatorId(typeId, op) : null)],
        ConversionOperatorDeclarationSyntax conv => [CreateConversionOperatorMember(conv, typeId)],
        EventFieldDeclarationSyntax eventField => ExtractEventFieldMembers(eventField, typeId),
        EventDeclarationSyntax eventDecl => [CreateEventMember(eventDecl, typeId)],
        IndexerDeclarationSyntax indexer => [CreateIndexerMember(indexer, typeId)],
        _ => []
    };

    static IEnumerable<MemberInfo> ExtractFieldMembers(FieldDeclarationSyntax field, string? typeId)
    {
        var fieldType = field.Declaration.Type.ToString();
        var modifiers = GetModifiers(field.Modifiers);
        var attrs = GetAttributeInfos(field.AttributeLists);
        foreach (var variable in field.Declaration.Variables)
        {
            var location = ComputeLocation(variable);
            var memberId = typeId is not null ? MemberIdentity.CreateFieldId(typeId, variable.Identifier.Text) : null;
            yield return new MemberInfo(variable.Identifier.Text, fieldType, "Field", modifiers, [], attrs, Location: location, MemberId: memberId);
        }
    }

    static MemberInfo CreatePropertyMember(PropertyDeclarationSyntax prop, string? typeId) =>
        new(prop.Identifier.Text, prop.Type.ToString(), "Property",
            GetModifiers(prop.Modifiers), [], GetAttributeInfos(prop.AttributeLists),
            Location: ComputeLocation(prop),
            MemberId: typeId is not null ? MemberIdentity.CreatePropertyId(typeId, prop.Identifier.Text) : null);

    static IEnumerable<MemberInfo> ExtractEventFieldMembers(EventFieldDeclarationSyntax eventField, string? typeId)
    {
        var eventType = eventField.Declaration.Type.ToString();
        var modifiers = GetModifiers(eventField.Modifiers);
        var attrs = GetAttributeInfos(eventField.AttributeLists);
        foreach (var variable in eventField.Declaration.Variables)
        {
            var location = ComputeLocation(variable);
            var memberId = typeId is not null ? MemberIdentity.CreateEventId(typeId, variable.Identifier.Text) : null;
            yield return new MemberInfo(variable.Identifier.Text, eventType, "Event", modifiers, [], attrs, Location: location, MemberId: memberId);
        }
    }

    static MemberInfo CreateEventMember(EventDeclarationSyntax eventDecl, string? typeId) =>
        new(eventDecl.Identifier.Text, eventDecl.Type.ToString(), "Event",
            GetModifiers(eventDecl.Modifiers), [], GetAttributeInfos(eventDecl.AttributeLists),
            Location: ComputeLocation(eventDecl),
            MemberId: typeId is not null ? MemberIdentity.CreateEventId(typeId, eventDecl.Identifier.Text) : null);

    static MemberInfo CreateIndexerMember(IndexerDeclarationSyntax indexer, string? typeId)
    {
        var indexParams = indexer.ParameterList.Parameters
            .Select(p => new ParameterInfo(p.Identifier.Text, p.Type?.ToString() ?? "unknown"))
            .ToList();
        return new MemberInfo("this[]", indexer.Type.ToString(), "Indexer",
            GetModifiers(indexer.Modifiers), indexParams, GetAttributeInfos(indexer.AttributeLists),
            Location: ComputeLocation(indexer),
            MemberId: typeId is not null ? MemberIdentity.CreateIndexerId(typeId, indexer) : null);
    }

    internal static MemberInfo CreateMethodMember(MethodDeclarationSyntax method, string? typeId = null)
    {
        var methodParams = method.ParameterList.Parameters
            .Select(p => new ParameterInfo(p.Identifier.Text, p.Type?.ToString() ?? "unknown"))
            .ToList();
        var memberId = typeId is not null ? MemberIdentity.CreateMethodId(typeId, method) : null;
        return CreateBodyBearingMember(method, method.Identifier.Text, method.ReturnType.ToString(), "Method",
            methodParams, method.Modifiers, method.AttributeLists, memberId);
    }

    static MemberInfo CreateConversionOperatorMember(ConversionOperatorDeclarationSyntax conv, string? typeId)
    {
        var name = conv.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ImplicitKeyword) ? "op_Implicit" : "op_Explicit";
        var memberId = typeId is not null ? MemberIdentity.CreateConversionOperatorId(typeId, conv) : null;
        return CreateExecutableMember(conv, name, conv.Type.ToString(), "ConversionOperator",
            conv.ParameterList, conv.Modifiers, conv.AttributeLists, memberId);
    }

    static MemberInfo CreateExecutableMember(
        MemberDeclarationSyntax node, string name, string returnType, string memberKind,
        ParameterListSyntax? parameterList, SyntaxTokenList modifiers,
        SyntaxList<AttributeListSyntax> attributeLists, string? memberId)
    {
        var parameters = parameterList?.Parameters
            .Select(p => new ParameterInfo(p.Identifier.Text, p.Type?.ToString() ?? "unknown"))
            .ToList() ?? [];
        return CreateBodyBearingMember(node, name, returnType, memberKind,
            parameters, modifiers, attributeLists, memberId);
    }

    static MemberInfo CreateBodyBearingMember(
        SyntaxNode node, string name, string returnType, string memberKind,
        IReadOnlyList<ParameterInfo> parameters, SyntaxTokenList modifiers,
        SyntaxList<AttributeListSyntax> attributeLists, string? memberId)
    {
        var bodyNode = node switch
        {
            BaseMethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
            _ => null
        };
        var (cogCC, cycCC, nestDepth, halstead) = MethodMetricsCalculator.Calculate(bodyNode);
        var span = node.GetLocation().GetLineSpan();
        var lineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
        var startLine = span.StartLinePosition.Line + 1;
        return new MemberInfo(name, returnType, memberKind,
            GetModifiers(modifiers), parameters, GetAttributeInfos(attributeLists),
            cogCC, cycCC, nestDepth, lineCount, startLine,
            halstead.Volume, halstead.Difficulty, halstead.Effort, halstead.EstimatedBugs,
            ComputeLocation(node), memberId);
    }

    internal static IEnumerable<string> ExtractConstructorParams(TypeDeclarationSyntax typeDecl)
    {
        foreach (var ctor in typeDecl.Members.OfType<ConstructorDeclarationSyntax>())
            foreach (var param in ctor.ParameterList.Parameters)
                yield return param.Type?.ToString() ?? "unknown";
    }

    internal static IReadOnlyList<GenericConstraintInfo> ExtractGenericConstraints(TypeDeclarationSyntax typeDecl)
    {
        if (typeDecl.ConstraintClauses.Count == 0) return [];
        return typeDecl.ConstraintClauses
            .Select(cc => new GenericConstraintInfo(
                cc.Name.ToString(), cc.Constraints.Select(c => c.ToString()).ToList()))
            .ToList();
    }

    internal static SourceLocation? ComputeLocation(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        if (!span.IsValid)
            return null;
        return new SourceLocation(FileRef: 0, StartLine: span.StartLinePosition.Line + 1, EndLine: span.EndLinePosition.Line + 1);
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
