namespace Unilyze;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class AsyncFlowEnclosingMemberName
{
    public static string Get(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            var name = TryGetName(ancestor);
            if (name is not null)
                return name;
        }

        return "<unknown>";
    }

    static string? TryGetName(SyntaxNode ancestor) => ancestor switch
    {
        MethodDeclarationSyntax method => method.Identifier.Text,
        LocalFunctionStatementSyntax localFn => localFn.Identifier.Text,
        ConstructorDeclarationSyntax ctor => ctor.Identifier.Text,
        PropertyDeclarationSyntax prop => prop.Identifier.Text,
        _ => null,
    };
}
