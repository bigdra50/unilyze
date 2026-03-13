using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests;

internal static class RoslynTestHelper
{
    public static SyntaxTree ParseCode(string code)
    {
        return CSharpSyntaxTree.ParseText(code,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
    }

    public static MethodDeclarationSyntax GetMethod(string code, string name)
    {
        var tree = ParseCode(code);
        var root = tree.GetRoot();
        return root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == name);
    }

    public static SyntaxNode? GetMethodBody(string code, string name)
    {
        var method = GetMethod(code, name);
        return (SyntaxNode?)method.Body ?? method.ExpressionBody;
    }

    public static TypeDeclarationSyntax GetType(string code, string name)
    {
        var tree = ParseCode(code);
        var root = tree.GetRoot();
        return root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == name);
    }

    public static SemanticModel CreateSemanticModel(string code)
    {
        var tree = ParseCode(code);
        var compilation = CSharpCompilation.Create("Test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation.GetSemanticModel(tree);
    }

    public static IReadOnlyList<TypeNodeInfo> ExtractTypesFromCode(string code)
    {
        var tree = ParseCode(code);
        var root = tree.GetRoot();
        var rawTypes = new List<TypeNodeInfo>();
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            var name = typeDecl.Identifier.Text;
            if (typeDecl.TypeParameterList is { } tpl)
                name += $"<{string.Join(",", tpl.Parameters.Select(p => p.Identifier.Text))}>";

            var kind = typeDecl switch
            {
                RecordDeclarationSyntax r => r.ClassOrStructKeyword.Text == "struct" ? "record struct" : "record",
                ClassDeclarationSyntax => "class",
                StructDeclarationSyntax => "struct",
                InterfaceDeclarationSyntax => "interface",
                _ => "type"
            };

            var ns = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "";
            var modifiers = typeDecl.Modifiers.Select(m => m.Text).ToList();
            var members = ExtractMembersFromType(typeDecl);
            var typeSpan = typeDecl.GetLocation().GetLineSpan();
            var typeLineCount = typeSpan.EndLinePosition.Line - typeSpan.StartLinePosition.Line + 1;
            var typeStartLine = typeSpan.StartLinePosition.Line + 1;

            rawTypes.Add(new TypeNodeInfo(
                name, ns, kind, modifiers, null, [], members, [],
                [], [], null, "TestAssembly", "test.cs", false, typeLineCount, typeStartLine));
        }
        return rawTypes;
    }

    static IReadOnlyList<MemberInfo> ExtractMembersFromType(TypeDeclarationSyntax typeDecl)
    {
        var members = new List<MemberInfo>();
        foreach (var member in typeDecl.Members)
        {
            if (member is MethodDeclarationSyntax method)
            {
                var methodParams = method.ParameterList.Parameters
                    .Select(p => new ParameterInfo(p.Identifier.Text, p.Type?.ToString() ?? "unknown"))
                    .ToList();
                var bodyNode = (SyntaxNode?)method.Body ?? method.ExpressionBody;
                var cogCC = bodyNode != null ? CognitiveComplexity.Calculate(bodyNode) : 0;
                var cycCC = bodyNode != null ? CyclomaticComplexity.Calculate(bodyNode) : 1;
                var nestDepth = bodyNode != null ? NestingDepth.Calculate(bodyNode) : 0;
                var halstead = HalsteadCalculator.Calculate(bodyNode);
                var methodSpan = method.GetLocation().GetLineSpan();
                var methodLineCount = methodSpan.EndLinePosition.Line - methodSpan.StartLinePosition.Line + 1;
                var methodStartLine = methodSpan.StartLinePosition.Line + 1;
                members.Add(new MemberInfo(
                    method.Identifier.Text, method.ReturnType.ToString(), "Method",
                    method.Modifiers.Select(m => m.Text).ToList(), methodParams, [],
                    cogCC, cycCC, nestDepth, methodLineCount, methodStartLine, halstead.Volume));
            }
        }
        return members;
    }
}
