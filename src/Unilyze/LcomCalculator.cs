using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public static class LcomCalculator
{
    /// <summary>
    /// LCOM-HS (Henderson-Sellers variant).
    /// Returns 0.0 (fully cohesive) to 1.0+ (no cohesion).
    /// Returns null for types with 0-1 methods or 0 fields.
    /// </summary>
    public static double? Calculate(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        var fields = CollectFields(typeDecl);
        if (fields.Count == 0) return null;

        var methods = typeDecl.Members.OfType<MethodDeclarationSyntax>().ToList();
        if (methods.Count <= 1) return null;

        var fieldAccess = new Dictionary<string, HashSet<string>>();
        foreach (var method in methods)
        {
            var accessed = model is not null
                ? CollectFieldAccessesSemantic(method, model, fields)
                : CollectFieldAccessesSyntactic(method, fields);
            fieldAccess[method.Identifier.Text] = accessed;
        }

        // LCOM-HS = (avg(fieldAccessCount) - m) / (1 - m)
        // where m = number of methods, fieldAccessCount = methods accessing each field
        var m = methods.Count;
        var totalAccess = 0.0;
        foreach (var field in fields)
        {
            var count = fieldAccess.Values.Count(accessedFields => accessedFields.Contains(field));
            totalAccess += count;
        }

        var avgAccess = totalAccess / fields.Count;
        if (m <= 1) return null;

        var lcom = (avgAccess - m) / (1.0 - m);
        return Math.Round(Math.Max(0.0, lcom), 2);
    }

    static HashSet<string> CollectFields(TypeDeclarationSyntax typeDecl)
    {
        var fields = new HashSet<string>();

        foreach (var field in typeDecl.Members.OfType<FieldDeclarationSyntax>())
        {
            foreach (var variable in field.Declaration.Variables)
                fields.Add(variable.Identifier.Text);
        }

        // Auto-properties with backing fields
        foreach (var prop in typeDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (prop.AccessorList?.Accessors.Any(a =>
                a.Body is null && a.ExpressionBody is null &&
                a.IsKind(SyntaxKind.GetAccessorDeclaration)) == true)
            {
                fields.Add(prop.Identifier.Text);
            }
        }

        return fields;
    }

    static HashSet<string> CollectFieldAccessesSemantic(
        MethodDeclarationSyntax method, SemanticModel model, HashSet<string> fields)
    {
        var accessed = new HashSet<string>();
        var body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        if (body is null) return accessed;

        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var symbolInfo = model.GetSymbolInfo(identifier);
            var symbol = symbolInfo.Symbol;
            if (symbol is null) continue;

            switch (symbol)
            {
                case IFieldSymbol fieldSymbol
                    when fields.Contains(fieldSymbol.Name):
                    accessed.Add(fieldSymbol.Name);
                    break;

                case IPropertySymbol propSymbol
                    when fields.Contains(propSymbol.Name):
                    accessed.Add(propSymbol.Name);
                    break;
            }
        }

        return accessed;
    }

    static HashSet<string> CollectFieldAccessesSyntactic(
        MethodDeclarationSyntax method, HashSet<string> fields)
    {
        var accessed = new HashSet<string>();
        var body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        if (body is null) return accessed;

        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var name = identifier.Identifier.Text;
            if (fields.Contains(name))
                accessed.Add(name);
        }

        // this.field access
        foreach (var memberAccess in body.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (memberAccess.Expression is ThisExpressionSyntax)
            {
                var name = memberAccess.Name.Identifier.Text;
                if (fields.Contains(name))
                    accessed.Add(name);
            }
        }

        return accessed;
    }
}
