using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

internal static class ApiSurfaceExtractor
{
    internal const int DocSummaryMaxLength = 500;

    public static IReadOnlyList<TypeApiSurface> Extract(
        IReadOnlyList<SyntaxTree> syntaxTrees,
        IReadOnlyList<TypeNodeInfo> types)
    {
        var fileToAssembly = types
            .GroupBy(t => t.FilePath, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Assembly, StringComparer.Ordinal);

        var surfaces = new List<TypeApiSurface>();
        foreach (var tree in syntaxTrees)
        {
            var assembly = fileToAssembly.GetValueOrDefault(tree.FilePath, "Assembly-CSharp");
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                switch (node)
                {
                    case TypeDeclarationSyntax typeDecl:
                        surfaces.Add(ExtractTypeSurface(typeDecl, assembly));
                        break;
                    case EnumDeclarationSyntax enumDecl:
                        surfaces.Add(ExtractEnumSurface(enumDecl, assembly));
                        break;
                    case DelegateDeclarationSyntax delegateDecl:
                        surfaces.Add(ExtractDelegateSurface(delegateDecl, assembly));
                        break;
                }
            }
        }

        return surfaces
            .OrderBy(s => s.TypeId, StringComparer.Ordinal)
            .ToList();
    }

    static TypeApiSurface ExtractTypeSurface(TypeDeclarationSyntax typeDecl, string assembly)
    {
        var typeId = TypeIdentity.CreateTypeId(typeDecl, assembly);
        var qualifiedName = TypeIdentity.CreateQualifiedName(typeDecl);
        var (hasDoc, summary) = ExtractDocSummary(typeDecl);
        var identifiers = new List<string> { typeDecl.Identifier.Text };
        CollectTypeParameterNames(typeDecl.TypeParameterList, identifiers);

        var publicSignatures = new List<string>();
        if (IsPublicOrProtected(typeDecl.Modifiers))
            publicSignatures.Add(RenderTypeDeclaration(typeDecl));

        var documentedPublic = 0;
        var publicMemberCount = 0;

        foreach (var member in typeDecl.Members)
        {
            CollectMemberIdentifiers(member, identifiers);
            if (!IsPublicOrProtectedMember(member))
                continue;

            publicMemberCount++;
            var (memberHasDoc, _) = ExtractDocSummary(member);
            if (memberHasDoc)
                documentedPublic++;

            if (IsPublicOrProtectedMember(member))
                publicSignatures.Add(RenderMemberSignature(member));
        }

        return new TypeApiSurface(
            typeId,
            qualifiedName,
            hasDoc,
            summary,
            publicSignatures,
            identifiers.Distinct(StringComparer.Ordinal).OrderBy(i => i, StringComparer.Ordinal).ToList(),
            documentedPublic,
            publicMemberCount);
    }

    static TypeApiSurface ExtractEnumSurface(EnumDeclarationSyntax enumDecl, string assembly)
    {
        var typeId = TypeIdentity.CreateTypeId(enumDecl, assembly);
        var qualifiedName = TypeIdentity.CreateQualifiedName(enumDecl);
        var (hasDoc, summary) = ExtractDocSummary(enumDecl);
        var identifiers = new List<string> { enumDecl.Identifier.Text };
        var publicSignatures = new List<string>();

        if (IsPublicOrProtected(enumDecl.Modifiers))
            publicSignatures.Add(RenderEnumDeclaration(enumDecl));

        foreach (var member in enumDecl.Members)
            identifiers.Add(member.Identifier.Text);

        return new TypeApiSurface(
            typeId,
            qualifiedName,
            hasDoc,
            summary,
            publicSignatures,
            identifiers.Distinct(StringComparer.Ordinal).OrderBy(i => i, StringComparer.Ordinal).ToList(),
            0,
            0);
    }

    static TypeApiSurface ExtractDelegateSurface(DelegateDeclarationSyntax delegateDecl, string assembly)
    {
        var typeId = TypeIdentity.CreateTypeId(delegateDecl, assembly);
        var qualifiedName = TypeIdentity.CreateQualifiedName(delegateDecl);
        var (hasDoc, summary) = ExtractDocSummary(delegateDecl);
        var identifiers = new List<string> { delegateDecl.Identifier.Text };
        CollectTypeParameterNames(delegateDecl.TypeParameterList, identifiers);
        CollectParameterNames(delegateDecl.ParameterList, identifiers);

        var publicSignatures = new List<string>();
        if (IsPublicOrProtected(delegateDecl.Modifiers))
            publicSignatures.Add(RenderDelegateDeclaration(delegateDecl));

        return new TypeApiSurface(
            typeId,
            qualifiedName,
            hasDoc,
            summary,
            publicSignatures,
            identifiers.Distinct(StringComparer.Ordinal).OrderBy(i => i, StringComparer.Ordinal).ToList(),
            0,
            0);
    }

    internal static (bool HasDocComment, string? DocSummary) ExtractDocSummary(SyntaxNode node)
    {
        var doc = node.GetLeadingTrivia()
            .Select(t => t.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();

        if (doc is null)
            return (false, null);

        if (doc.Content.Any(c => c is XmlEmptyElementSyntax empty && empty.Name.LocalName.ValueText == "inheritdoc"))
            return (true, null);

        var summaryText = ExtractSummaryText(doc);
        if (summaryText is null)
            return (true, null);

        return (true, NormalizeSummary(summaryText));
    }

    static string? ExtractSummaryText(DocumentationCommentTriviaSyntax doc)
    {
        foreach (var content in doc.Content)
        {
            if (content is not XmlElementSyntax { StartTag.Name.LocalName.ValueText: "summary" } summary)
                continue;

            return ExtractXmlText(summary);
        }

        return null;
    }

    static string ExtractXmlText(XmlElementSyntax element)
    {
        var sb = new StringBuilder();
        foreach (var node in element.Content)
        {
            switch (node)
            {
                case XmlTextSyntax text:
                    sb.Append(text.TextTokens.Select(t => t.ToString()).Aggregate("", string.Concat));
                    break;
                case XmlElementSyntax nested:
                    sb.Append(ExtractXmlText(nested));
                    break;
                case XmlCDataSectionSyntax cdata:
                    sb.Append(cdata.TextTokens.Select(t => t.ToString()).Aggregate("", string.Concat));
                    break;
            }
        }

        return sb.ToString();
    }

    static string? NormalizeSummary(string raw)
    {
        var normalized = string.Join(
            " ",
            raw.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (normalized.Length == 0)
            return null;

        if (normalized.Length <= DocSummaryMaxLength)
            return normalized;

        return normalized[..DocSummaryMaxLength] + "…";
    }

    static void CollectTypeParameterNames(TypeParameterListSyntax? typeParams, List<string> identifiers)
    {
        if (typeParams is null)
            return;

        foreach (var param in typeParams.Parameters)
            identifiers.Add(param.Identifier.Text);
    }

    static void CollectParameterNames(BaseParameterListSyntax? paramList, List<string> identifiers)
    {
        if (paramList is null)
            return;

        foreach (var param in paramList.Parameters)
            identifiers.Add(param.Identifier.Text);
    }

    static void CollectMemberIdentifiers(MemberDeclarationSyntax member, List<string> identifiers)
    {
        switch (member)
        {
            case MethodDeclarationSyntax method:
                identifiers.Add(method.Identifier.Text);
                CollectTypeParameterNames(method.TypeParameterList, identifiers);
                CollectParameterNames(method.ParameterList, identifiers);
                break;
            case PropertyDeclarationSyntax property:
                identifiers.Add(property.Identifier.Text);
                break;
            case EventDeclarationSyntax eventDecl:
                identifiers.Add(eventDecl.Identifier.Text);
                break;
            case IndexerDeclarationSyntax indexer:
                CollectParameterNames(indexer.ParameterList, identifiers);
                break;
            case FieldDeclarationSyntax field:
                foreach (var variable in field.Declaration.Variables)
                    identifiers.Add(variable.Identifier.Text);
                break;
            case EventFieldDeclarationSyntax eventField:
                foreach (var variable in eventField.Declaration.Variables)
                    identifiers.Add(variable.Identifier.Text);
                break;
            case ConstructorDeclarationSyntax ctor:
                CollectParameterNames(ctor.ParameterList, identifiers);
                break;
        }
    }

    static bool IsPublicOrProtected(SyntaxTokenList modifiers) =>
        modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword));

    static bool IsPublicOrProtectedMember(MemberDeclarationSyntax member) =>
        member switch
        {
            MethodDeclarationSyntax m => IsPublicOrProtected(m.Modifiers),
            PropertyDeclarationSyntax p => IsPublicOrProtected(p.Modifiers),
            FieldDeclarationSyntax f => IsPublicOrProtected(f.Modifiers),
            EventDeclarationSyntax e => IsPublicOrProtected(e.Modifiers),
            EventFieldDeclarationSyntax ef => IsPublicOrProtected(ef.Modifiers),
            IndexerDeclarationSyntax i => IsPublicOrProtected(i.Modifiers),
            ConstructorDeclarationSyntax c => IsPublicOrProtected(c.Modifiers),
            _ => false
        };

    static string RenderTypeDeclaration(TypeDeclarationSyntax typeDecl)
    {
        var kind = typeDecl switch
        {
            RecordDeclarationSyntax r => r.ClassOrStructKeyword.Text == "struct" ? "record struct" : "record",
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            _ => "type"
        };

        var name = RenderTypeName(typeDecl.Identifier.Text, typeDecl.TypeParameterList);
        var modifiers = string.Join(" ", MemberExtractor.GetModifiers(typeDecl.Modifiers));
        var bases = RenderBaseList(typeDecl.BaseList, typeDecl is InterfaceDeclarationSyntax);
        return string.IsNullOrEmpty(bases)
            ? $"{modifiers} {kind} {name}".Trim()
            : $"{modifiers} {kind} {name} {bases}".Trim();
    }

    static string RenderEnumDeclaration(EnumDeclarationSyntax enumDecl)
    {
        var modifiers = string.Join(" ", MemberExtractor.GetModifiers(enumDecl.Modifiers));
        var bases = enumDecl.BaseList is null ? "" : $": {enumDecl.BaseList}";
        return $"{modifiers} enum {enumDecl.Identifier.Text}{bases}".Trim();
    }

    static string RenderDelegateDeclaration(DelegateDeclarationSyntax delegateDecl)
    {
        var modifiers = string.Join(" ", MemberExtractor.GetModifiers(delegateDecl.Modifiers));
        var name = RenderTypeName(delegateDecl.Identifier.Text, delegateDecl.TypeParameterList);
        var parameters = RenderParameterList(delegateDecl.ParameterList);
        return $"{modifiers} delegate {delegateDecl.ReturnType} {name}{parameters}".Trim();
    }

    static string RenderMemberSignature(MemberDeclarationSyntax member) =>
        member switch
        {
            MethodDeclarationSyntax method =>
                $"{string.Join(" ", MemberExtractor.GetModifiers(method.Modifiers))} {RenderMethodName(method)}",
            PropertyDeclarationSyntax property =>
                $"{string.Join(" ", MemberExtractor.GetModifiers(property.Modifiers))} {property.Type} {property.Identifier.Text} {{ ... }}",
            FieldDeclarationSyntax field =>
                $"{string.Join(" ", MemberExtractor.GetModifiers(field.Modifiers))} {field.Declaration}",
            EventDeclarationSyntax eventDecl =>
                $"{string.Join(" ", MemberExtractor.GetModifiers(eventDecl.Modifiers))} event {eventDecl.Type} {eventDecl.Identifier.Text}",
            EventFieldDeclarationSyntax eventField =>
                $"{string.Join(" ", MemberExtractor.GetModifiers(eventField.Modifiers))} {eventField.Declaration}",
            IndexerDeclarationSyntax indexer =>
                $"{string.Join(" ", MemberExtractor.GetModifiers(indexer.Modifiers))} {indexer.Type} this{RenderParameterList(indexer.ParameterList)} {{ ... }}",
            ConstructorDeclarationSyntax ctor =>
                $"{string.Join(" ", MemberExtractor.GetModifiers(ctor.Modifiers))} {ctor.Identifier.Text}{RenderParameterList(ctor.ParameterList)}",
            _ => member.ToString().Split('{')[0].TrimEnd()
        };

    static string RenderMethodName(MethodDeclarationSyntax method)
    {
        var typeParams = method.TypeParameterList is null ? "" : method.TypeParameterList.ToString();
        var parameters = RenderParameterList(method.ParameterList);
        return $"{method.ReturnType} {method.Identifier.Text}{typeParams}{parameters}";
    }

    static string RenderTypeName(string identifier, TypeParameterListSyntax? typeParams)
    {
        if (typeParams is null || typeParams.Parameters.Count == 0)
            return identifier;

        var args = string.Join(", ", typeParams.Parameters.Select(p => p.Identifier.Text));
        return $"{identifier}<{args}>";
    }

    static string RenderParameterList(BaseParameterListSyntax? paramList) =>
        paramList?.ToString() ?? "()";

    static string RenderBaseList(BaseListSyntax? baseList, bool isInterface)
    {
        if (baseList is null || baseList.Types.Count == 0)
            return "";

        var items = baseList.Types.Select(t => t.ToString()).ToList();
        if (isInterface)
            return $": {string.Join(", ", items)}";

        if (items.Count == 1)
            return $": {items[0]}";

        return $": {items[0]}, {string.Join(", ", items[1..])}";
    }
}
