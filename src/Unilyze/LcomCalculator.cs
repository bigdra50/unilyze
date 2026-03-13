using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public static class LcomCalculator
{
    /// <summary>
    /// LCOM-HS (Henderson-Sellers variant).
    /// Returns 0.0 (fully cohesive) to 1.0+ (no cohesion).
    /// Returns null for types with 0-1 instance methods or 0 instance fields.
    /// </summary>
    public static double? Calculate(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        var fields = CollectInstanceFields(typeDecl);
        if (fields.Count == 0) return null;

        var methodBodies = new List<SyntaxNode?>();
        foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>()
            .Where(m => !m.Modifiers.Any(SyntaxKind.StaticKeyword)))
            methodBodies.Add((SyntaxNode?)method.Body ?? method.ExpressionBody);
        foreach (var ctor in typeDecl.Members.OfType<ConstructorDeclarationSyntax>()
            .Where(c => !c.Modifiers.Any(SyntaxKind.StaticKeyword)))
            methodBodies.Add((SyntaxNode?)ctor.Body ?? ctor.ExpressionBody);

        if (methodBodies.Count <= 1) return null;

        INamedTypeSymbol? containingType = null;
        if (model is not null)
            containingType = model.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;

        var fieldAccessSets = new List<HashSet<string>>(methodBodies.Count);
        foreach (var body in methodBodies)
        {
            var accessed = model is not null
                ? CollectFieldAccessesSemantic(body, model, fields, containingType)
                : CollectFieldAccessesSyntactic(body, fields);
            fieldAccessSets.Add(accessed);
        }

        // LCOM-HS = (1/a * SUM(mA(f)) - m) / (1 - m)
        // a = field count, m = method count, mA(f) = methods accessing field f
        var m = methodBodies.Count;
        var totalAccess = 0.0;
        foreach (var field in fields)
        {
            var count = fieldAccessSets.Count(set => set.Contains(field));
            totalAccess += count;
        }

        var avgAccess = totalAccess / fields.Count;
        var lcom = (avgAccess - m) / (1.0 - m);
        return Math.Round(Math.Max(0.0, lcom), 2);
    }

    static HashSet<string> CollectInstanceFields(TypeDeclarationSyntax typeDecl)
    {
        var fields = new HashSet<string>();

        foreach (var field in typeDecl.Members.OfType<FieldDeclarationSyntax>())
        {
            if (field.Modifiers.Any(SyntaxKind.StaticKeyword)) continue;
            foreach (var variable in field.Declaration.Variables)
                fields.Add(variable.Identifier.Text);
        }

        return fields;
    }

    static HashSet<string> CollectFieldAccessesSemantic(
        SyntaxNode? body, SemanticModel model, HashSet<string> fields,
        INamedTypeSymbol? containingType)
    {
        var accessed = new HashSet<string>();
        if (body is null) return accessed;

        foreach (var identifier in body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var symbolInfo = model.GetSymbolInfo(identifier);
            var symbol = symbolInfo.Symbol;
            if (symbol is null) continue;

            switch (symbol)
            {
                case IFieldSymbol fieldSymbol
                    when fields.Contains(fieldSymbol.Name)
                    && (containingType is null
                        || SymbolEqualityComparer.Default.Equals(
                            fieldSymbol.ContainingType, containingType)):
                    accessed.Add(fieldSymbol.Name);
                    break;

                case IPropertySymbol propSymbol
                    when fields.Contains(propSymbol.Name)
                    && (containingType is null
                        || SymbolEqualityComparer.Default.Equals(
                            propSymbol.ContainingType, containingType)):
                    accessed.Add(propSymbol.Name);
                    break;
            }
        }

        return accessed;
    }

    static HashSet<string> CollectFieldAccessesSyntactic(
        SyntaxNode? body, HashSet<string> fields)
    {
        var accessed = new HashSet<string>();
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
