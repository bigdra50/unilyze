using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public sealed record TypeDependency(
    string FromType,
    string ToType,
    DependencyKind Kind,
    string? FromTypeId = null,
    string? ToTypeId = null);

public enum DependencyKind
{
    Inheritance,
    InterfaceImpl,
    FieldType,
    PropertyType,
    ConstructorParam,
    MethodParam,
    ReturnType,
    EventType,
    GenericConstraint
}

public sealed record ParameterInfo(string Name, string Type);

public sealed record AttributeInfo(string Name, IReadOnlyDictionary<string, string>? Arguments);

public sealed record GenericConstraintInfo(string TypeParameter, IReadOnlyList<string> Constraints);

public sealed record TypeNodeInfo(
    string Name,
    string Namespace,
    string Kind,
    IReadOnlyList<string> Modifiers,
    string? BaseType,
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<MemberInfo> Members,
    IReadOnlyList<string> ConstructorParams,
    IReadOnlyList<AttributeInfo> Attributes,
    IReadOnlyList<GenericConstraintInfo> GenericConstraints,
    string? EnumBaseType,
    string Assembly,
    string FilePath,
    bool IsNested,
    int LineCount = 0,
    int? StartLine = null,
    string? QualifiedName = null,
    string? TypeId = null);

public sealed record MemberInfo(
    string Name,
    string Type,
    string MemberKind,
    IReadOnlyList<string> Modifiers,
    IReadOnlyList<ParameterInfo> Parameters,
    IReadOnlyList<AttributeInfo> Attributes,
    int? CognitiveComplexity = null,
    int? CyclomaticComplexity = null,
    int? MaxNestingDepth = null,
    int LineCount = 0,
    int? StartLine = null,
    double? HalsteadVolume = null);

public sealed record AnalyzeDirectoryResult(
    IReadOnlyList<TypeNodeInfo> Types,
    IReadOnlyList<SyntaxTree> SyntaxTrees);

public static class TypeAnalyzer
{
    public static IReadOnlyList<TypeNodeInfo> AnalyzeDirectory(string directory, string assemblyName)
        => AnalyzeDirectoryWithTrees(directory, assemblyName).Types;

    public static AnalyzeDirectoryResult AnalyzeDirectoryWithTrees(
        string directory, string assemblyName, IReadOnlyList<string>? preprocessorSymbols = null)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest);
        if (preprocessorSymbols is { Count: > 0 })
            parseOptions = parseOptions.WithPreprocessorSymbols(preprocessorSymbols);

        var csFiles = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories);
        var rawTypes = new List<TypeNodeInfo>();
        var trees = new List<SyntaxTree>();

        foreach (var file in csFiles)
        {
            var source = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(source, options: parseOptions, path: file);
            trees.Add(tree);
            var root = tree.GetRoot();
            rawTypes.AddRange(ExtractTypes(root, assemblyName, file));
        }

        // B2: 2パス interface 判定
        var knownInterfaces = new HashSet<string>(
            rawTypes.Where(t => t.Kind == "interface").Select(t => t.Name.Split('<')[0]));
        var resolved = rawTypes.Select(t => ResolveBaseTypes(t, knownInterfaces)).ToList();

        // B4: partial マージ
        return new AnalyzeDirectoryResult(MergePartialTypes(resolved), trees);
    }

    // --- Phase 1: Raw extraction ---

    static IEnumerable<TypeNodeInfo> ExtractTypes(SyntaxNode root, string assemblyName, string filePath)
    {
        foreach (var t in ExtractTypeDeclarations(root, assemblyName, filePath)) yield return t;
        foreach (var t in ExtractEnumDeclarations(root, assemblyName, filePath)) yield return t;
        foreach (var t in ExtractDelegateDeclarations(root, assemblyName, filePath)) yield return t;
    }

    static IEnumerable<TypeNodeInfo> ExtractTypeDeclarations(SyntaxNode root, string assemblyName, string filePath)
    {
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            var name = typeDecl.Identifier.Text;
            if (typeDecl.TypeParameterList is { } tpl)
                name += $"<{string.Join(",", tpl.Parameters.Select(p => p.Identifier.Text))}>";

            var kind = GetTypeKind(typeDecl);
            var ns = GetNamespace(typeDecl);
            var qualifiedName = TypeIdentity.CreateQualifiedName(typeDecl);
            var typeId = TypeIdentity.CreateTypeId(typeDecl, assemblyName);
            var modifiers = GetModifiers(typeDecl.Modifiers);
            var attributes = GetAttributeInfos(typeDecl.AttributeLists);
            var genericConstraints = ExtractGenericConstraints(typeDecl);
            var isNested = typeDecl.Parent is TypeDeclarationSyntax;

            var baseListItems = typeDecl.BaseList is { } baseList
                ? baseList.Types.Select(t => t.Type.ToString()).ToList()
                : [];

            var members = ExtractMembers(typeDecl).ToList();
            var ctorParams = ExtractConstructorParams(typeDecl).ToList();
            AddRecordParameters(typeDecl, members, ctorParams);

            var (baseType, interfaces) = SplitBaseList(baseListItems, typeDecl is InterfaceDeclarationSyntax);

            var typeSpan = typeDecl.GetLocation().GetLineSpan();
            var typeLineCount = typeSpan.EndLinePosition.Line - typeSpan.StartLinePosition.Line + 1;
            var typeStartLine = typeSpan.StartLinePosition.Line + 1;

            yield return new TypeNodeInfo(
                name, ns, kind, modifiers, baseType, interfaces, members, ctorParams,
                attributes, genericConstraints, null, assemblyName, filePath, isNested, typeLineCount, typeStartLine,
                qualifiedName, typeId);
        }
    }

    static string GetTypeKind(TypeDeclarationSyntax typeDecl) => typeDecl switch
    {
        RecordDeclarationSyntax r => r.ClassOrStructKeyword.Text == "struct" ? "record struct" : "record",
        ClassDeclarationSyntax => "class",
        StructDeclarationSyntax => "struct",
        InterfaceDeclarationSyntax => "interface",
        _ => "type"
    };

    static void AddRecordParameters(TypeDeclarationSyntax typeDecl, List<MemberInfo> members, List<string> ctorParams)
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

    static (string? BaseType, List<string> Interfaces) SplitBaseList(List<string> baseListItems, bool isInterface)
    {
        if (baseListItems.Count == 0 || isInterface)
            return (null, baseListItems);

        return (baseListItems[0], baseListItems[1..]);
    }

    static IEnumerable<TypeNodeInfo> ExtractEnumDeclarations(SyntaxNode root, string assemblyName, string filePath)
    {
        foreach (var enumDecl in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
        {
            var ns = GetNamespace(enumDecl);
            var qualifiedName = TypeIdentity.CreateQualifiedName(enumDecl);
            var typeId = TypeIdentity.CreateTypeId(enumDecl, assemblyName);
            var modifiers = GetModifiers(enumDecl.Modifiers);
            var attributes = GetAttributeInfos(enumDecl.AttributeLists);
            var isNested = enumDecl.Parent is TypeDeclarationSyntax;

            string? enumBaseType = null;
            if (enumDecl.BaseList is { } enumBase)
                enumBaseType = enumBase.Types.FirstOrDefault()?.Type.ToString();

            var enumMembers = enumDecl.Members
                .Select(m =>
                {
                    var value = m.EqualsValue?.Value.ToString();
                    var memberType = value != null ? $"enum = {value}" : "enum";
                    return new MemberInfo(
                        m.Identifier.Text, memberType, "EnumMember", [], [],
                        GetAttributeInfos(m.AttributeLists));
                })
                .ToList();

            var enumSpan = enumDecl.GetLocation().GetLineSpan();
            var enumLineCount = enumSpan.EndLinePosition.Line - enumSpan.StartLinePosition.Line + 1;
            var enumStartLine = enumSpan.StartLinePosition.Line + 1;

            yield return new TypeNodeInfo(
                enumDecl.Identifier.Text, ns, "enum", modifiers, null, [], enumMembers, [],
                attributes, [], enumBaseType, assemblyName, filePath, isNested, enumLineCount, enumStartLine,
                qualifiedName, typeId);
        }
    }

    static IEnumerable<TypeNodeInfo> ExtractDelegateDeclarations(SyntaxNode root, string assemblyName, string filePath)
    {
        foreach (var delDecl in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
        {
            var name = delDecl.Identifier.Text;
            if (delDecl.TypeParameterList is { } tpl)
                name += $"<{string.Join(",", tpl.Parameters.Select(p => p.Identifier.Text))}>";

            var ns = GetNamespace(delDecl);
            var qualifiedName = TypeIdentity.CreateQualifiedName(delDecl);
            var typeId = TypeIdentity.CreateTypeId(delDecl, assemblyName);
            var modifiers = GetModifiers(delDecl.Modifiers);
            var attributes = GetAttributeInfos(delDecl.AttributeLists);
            var isNested = delDecl.Parent is TypeDeclarationSyntax;

            var parameters = delDecl.ParameterList.Parameters
                .Select(p => new ParameterInfo(p.Identifier.Text, p.Type?.ToString() ?? "unknown"))
                .ToList();

            var returnType = delDecl.ReturnType.ToString();
            var members = new List<MemberInfo>
            {
                new("Invoke", returnType, "Method", ["public"], parameters, [])
            };

            var genericConstraints = delDecl.ConstraintClauses
                .Select(cc => new GenericConstraintInfo(
                    cc.Name.ToString(), cc.Constraints.Select(c => c.ToString()).ToList()))
                .ToList();

            var delSpan = delDecl.GetLocation().GetLineSpan();
            var delLineCount = delSpan.EndLinePosition.Line - delSpan.StartLinePosition.Line + 1;
            var delStartLine = delSpan.StartLinePosition.Line + 1;

            yield return new TypeNodeInfo(
                name, ns, "delegate", modifiers, null, [], members, [],
                attributes, genericConstraints, null, assemblyName, filePath, isNested, delLineCount, delStartLine,
                qualifiedName, typeId);
        }
    }

    // --- Phase 2: Resolve base type vs interfaces ---

    static TypeNodeInfo ResolveBaseTypes(TypeNodeInfo type, HashSet<string> knownInterfaces)
    {
        if (type.Kind is "enum" or "delegate" or "interface") return type;
        if (type.BaseType == null) return type;

        var baseName = type.BaseType.Split('<')[0];
        if (knownInterfaces.Contains(baseName))
        {
            var newInterfaces = new List<string> { type.BaseType };
            newInterfaces.AddRange(type.Interfaces);
            return type with { BaseType = null, Interfaces = newInterfaces };
        }

        return type;
    }

    // --- B4: Partial type merging ---

    static IReadOnlyList<TypeNodeInfo> MergePartialTypes(IReadOnlyList<TypeNodeInfo> types)
    {
        var groups = types.GroupBy(TypeIdentity.GetTypeId);
        var result = new List<TypeNodeInfo>();

        foreach (var group in groups)
        {
            var parts = group.ToList();
            if (parts.Count == 1)
            {
                result.Add(parts[0]);
                continue;
            }

            var first = parts[0];
            result.Add(first with
            {
                BaseType = parts.Select(p => p.BaseType).FirstOrDefault(b => b != null) ?? first.BaseType,
                Interfaces = parts.SelectMany(p => p.Interfaces).Distinct().ToList(),
                Members = parts.SelectMany(p => p.Members).ToList(),
                ConstructorParams = parts.SelectMany(p => p.ConstructorParams).ToList(),
                Attributes = parts.SelectMany(p => p.Attributes).DistinctBy(a => a.Name).ToList(),
                GenericConstraints = parts.SelectMany(p => p.GenericConstraints).DistinctBy(c => c.TypeParameter).ToList()
            });
        }

        return result;
    }

    // --- Member extraction ---

    static IEnumerable<MemberInfo> ExtractMembers(TypeDeclarationSyntax typeDecl)
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

    static IEnumerable<string> ExtractConstructorParams(TypeDeclarationSyntax typeDecl)
    {
        foreach (var ctor in typeDecl.Members.OfType<ConstructorDeclarationSyntax>())
        {
            foreach (var param in ctor.ParameterList.Parameters)
                yield return param.Type?.ToString() ?? "unknown";
        }
    }

    // --- N4: Generic constraints ---

    static IReadOnlyList<GenericConstraintInfo> ExtractGenericConstraints(TypeDeclarationSyntax typeDecl)
    {
        if (typeDecl.ConstraintClauses.Count == 0) return [];
        return typeDecl.ConstraintClauses
            .Select(cc => new GenericConstraintInfo(
                cc.Name.ToString(), cc.Constraints.Select(c => c.ToString()).ToList()))
            .ToList();
    }

    static MemberInfo CreateMethodMember(MethodDeclarationSyntax method)
    {
        var methodParams = method.ParameterList.Parameters
            .Select(p => new ParameterInfo(p.Identifier.Text, p.Type?.ToString() ?? "unknown"))
            .ToList();
        var bodyNode = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        var (cogCC, cycCC, nestDepth, halsteadVolume) = MethodMetricsCalculator.Calculate(bodyNode);
        var methodSpan = method.GetLocation().GetLineSpan();
        var methodLineCount = methodSpan.EndLinePosition.Line - methodSpan.StartLinePosition.Line + 1;
        var methodStartLine = methodSpan.StartLinePosition.Line + 1;
        return new MemberInfo(
            method.Identifier.Text, method.ReturnType.ToString(), "Method",
            GetModifiers(method.Modifiers), methodParams,
            GetAttributeInfos(method.AttributeLists), cogCC, cycCC, nestDepth, methodLineCount, methodStartLine,
            halsteadVolume);
    }

    // --- Helpers ---

    static string GetNamespace(SyntaxNode node)
    {
        var ns = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        return ns?.Name.ToString() ?? "";
    }

    static IReadOnlyList<string> GetModifiers(SyntaxTokenList modifiers)
        => modifiers.Select(m => m.Text).ToList();

    // N5: Attribute extraction with arguments
    static IReadOnlyList<AttributeInfo> GetAttributeInfos(SyntaxList<AttributeListSyntax> attributeLists)
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

static class DependencyBuilder
{
    public static IReadOnlyList<TypeDependency> Build(IReadOnlyList<TypeNodeInfo> types)
    {
        var knownTypes = KnownTypeIndex.Create(types);
        var deps = new List<TypeDependency>();

        foreach (var type in types)
        {
            if (type.BaseType != null)
                AddDepsForTypeName(type, type.BaseType, DependencyKind.Inheritance, knownTypes, deps);

            foreach (var iface in type.Interfaces)
                AddDepsForTypeName(type, iface, DependencyKind.InterfaceImpl, knownTypes, deps);

            CollectMemberDeps(type, knownTypes, deps);
            CollectConstraintDeps(type, knownTypes, deps);
        }

        return deps
            .Where(d => d.FromTypeId != d.ToTypeId)
            .Distinct()
            .ToList();
    }

    static void CollectMemberDeps(TypeNodeInfo type, KnownTypeIndex knownTypes, List<TypeDependency> deps)
    {
        foreach (var member in type.Members)
        {
            var kind = member.MemberKind switch
            {
                "Field" => DependencyKind.FieldType,
                "Property" => DependencyKind.PropertyType,
                "Method" => DependencyKind.ReturnType,
                "Event" => DependencyKind.EventType,
                "Indexer" => DependencyKind.PropertyType,
                _ => DependencyKind.FieldType
            };
            AddDepsForTypeName(type, member.Type, kind, knownTypes, deps);

            foreach (var param in member.Parameters)
                AddDepsForTypeName(type, param.Type, DependencyKind.MethodParam, knownTypes, deps);
        }

        foreach (var ctorParam in type.ConstructorParams)
            AddDepsForTypeName(type, ctorParam, DependencyKind.ConstructorParam, knownTypes, deps);
    }

    static void CollectConstraintDeps(TypeNodeInfo type, KnownTypeIndex knownTypes, List<TypeDependency> deps)
    {
        foreach (var constraint in type.GenericConstraints)
        {
            foreach (var c in constraint.Constraints)
            {
                var constraintType = c.TrimEnd('?');
                if (constraintType is "class" or "struct" or "notnull" or "unmanaged" or "new()")
                    continue;
                AddDepsForTypeName(type, constraintType, DependencyKind.GenericConstraint, knownTypes, deps);
            }
        }
    }

    static void AddDepsForTypeName(TypeNodeInfo fromType, string typeName, DependencyKind kind,
        KnownTypeIndex knownTypes, List<TypeDependency> deps)
    {
        foreach (var extracted in ExtractAllTypeNames(typeName))
        {
            foreach (var target in knownTypes.Resolve(fromType, extracted))
            {
                deps.Add(new TypeDependency(
                    fromType.Name,
                    target.Name,
                    kind,
                    TypeIdentity.GetTypeId(fromType),
                    TypeIdentity.GetTypeId(target)));
            }
        }
    }

    static IReadOnlyList<string> ExtractAllTypeNames(string typeName)
    {
        typeName = typeName.TrimEnd('?');
        if (string.IsNullOrEmpty(typeName)) return [];

        var result = new List<string>();
        var angleBracket = typeName.IndexOf('<');
        if (angleBracket >= 0)
        {
            result.Add(typeName[..angleBracket]);
            if (angleBracket + 1 < typeName.Length - 1)
            {
                var inner = typeName[(angleBracket + 1)..^1];
                var parts = SplitGenericArgs(inner);
                foreach (var part in parts)
                    result.AddRange(ExtractAllTypeNames(part.Trim()));
            }
        }
        else
        {
            result.Add(typeName);
        }

        return result;
    }

    static string[] SplitGenericArgs(string args)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case '<': depth++; break;
                case '>': depth--; break;
                case ',' when depth == 0:
                    result.Add(args[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        result.Add(args[start..].Trim());
        return result.ToArray();
    }

    sealed class KnownTypeIndex
    {
        readonly Dictionary<string, TypeNodeInfo> _byId;
        readonly Dictionary<string, List<TypeNodeInfo>> _byQualifiedName;
        readonly Dictionary<string, List<TypeNodeInfo>> _bySimpleName;

        KnownTypeIndex(
            Dictionary<string, TypeNodeInfo> byId,
            Dictionary<string, List<TypeNodeInfo>> byQualifiedName,
            Dictionary<string, List<TypeNodeInfo>> bySimpleName)
        {
            _byId = byId;
            _byQualifiedName = byQualifiedName;
            _bySimpleName = bySimpleName;
        }

        public static KnownTypeIndex Create(IReadOnlyList<TypeNodeInfo> types)
        {
            var byId = new Dictionary<string, TypeNodeInfo>(types.Count);
            var byQualifiedName = new Dictionary<string, List<TypeNodeInfo>>(StringComparer.Ordinal);
            var bySimpleName = new Dictionary<string, List<TypeNodeInfo>>(StringComparer.Ordinal);

            foreach (var type in types)
            {
                var typeId = TypeIdentity.GetTypeId(type);
                byId.TryAdd(typeId, type);

                AddToLookup(byQualifiedName, TypeIdentity.GetQualifiedName(type), type);
                AddToLookup(bySimpleName, TypeIdentity.GetSimpleName(type), type);
            }

            return new KnownTypeIndex(byId, byQualifiedName, bySimpleName);
        }

        public IReadOnlyList<TypeNodeInfo> Resolve(TypeNodeInfo fromType, string rawTypeName)
        {
            var normalized = TypeIdentity.NormalizeTypeReference(rawTypeName);
            if (string.IsNullOrWhiteSpace(normalized))
                return [];

            var exactQualified = ResolveUnique(_byQualifiedName, normalized);
            if (exactQualified.Count > 0)
                return exactQualified;

            foreach (var prefix in TypeIdentity.GetContainingQualifiedNamePrefixes(fromType))
            {
                var nestedQualified = $"{prefix}.{normalized}";
                var nestedMatch = ResolveUnique(_byQualifiedName, nestedQualified);
                if (nestedMatch.Count > 0)
                    return nestedMatch;
            }

            if (!string.IsNullOrEmpty(fromType.Namespace))
            {
                var sameNamespace = ResolveUnique(_byQualifiedName, $"{fromType.Namespace}.{normalized}");
                if (sameNamespace.Count > 0)
                    return sameNamespace;
            }

            var simpleName = TypeIdentity.StripGenericArgs(normalized);
            return ResolveUnique(_bySimpleName, simpleName);
        }

        static IReadOnlyList<TypeNodeInfo> ResolveUnique(
            Dictionary<string, List<TypeNodeInfo>> lookup,
            string key)
        {
            if (!lookup.TryGetValue(key, out var matches) || matches.Count != 1)
                return [];

            return matches;
        }

        static void AddToLookup(
            Dictionary<string, List<TypeNodeInfo>> lookup,
            string key,
            TypeNodeInfo type)
        {
            if (!lookup.TryGetValue(key, out var items))
            {
                items = [];
                lookup[key] = items;
            }
            items.Add(type);
        }
    }
}
