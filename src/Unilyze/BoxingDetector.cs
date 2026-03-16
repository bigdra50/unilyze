using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public sealed record BoxingOccurrence(string MethodName, string Description, int Line);

public static class BoxingDetector
{
    static readonly HashSet<string> VirtualObjectMethods = new(StringComparer.Ordinal)
    {
        "GetHashCode", "ToString", "Equals", "GetType"
    };

    public static IReadOnlyList<BoxingOccurrence> Detect(
        TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (model is null)
            return [];

        var results = new List<BoxingOccurrence>();

        foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            var methodName = method.Identifier.Text;
            DetectInMember(method, methodName, model, results);
        }

        foreach (var ctor in typeDecl.Members.OfType<ConstructorDeclarationSyntax>())
        {
            var methodName = ctor.Identifier.Text + ".ctor";
            DetectInMember(ctor, methodName, model, results);
        }

        return results;
    }

    static void DetectInMember(SyntaxNode member, string methodName, SemanticModel model,
        List<BoxingOccurrence> results)
    {
        foreach (var node in member.DescendantNodes())
        {
            switch (node)
            {
                case ExpressionSyntax expr:
                    CheckBoxingConversion(expr, methodName, model, results);
                    break;
            }
        }

        // Check string interpolation boxing separately
        foreach (var interpolation in member.DescendantNodes().OfType<InterpolationSyntax>())
        {
            CheckInterpolationBoxing(interpolation, methodName, model, results);
        }

        // Check virtual method calls on structs
        foreach (var invocation in member.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            CheckVirtualCallOnStruct(invocation, methodName, model, results);
        }
    }

    static void CheckBoxingConversion(ExpressionSyntax expr, string methodName,
        SemanticModel model, List<BoxingOccurrence> results)
    {
        var typeInfo = model.GetTypeInfo(expr);
        if (typeInfo.Type is null || typeInfo.ConvertedType is null)
            return;
        if (!typeInfo.Type.IsValueType)
            return;
        if (SymbolEqualityComparer.Default.Equals(typeInfo.Type, typeInfo.ConvertedType))
            return;

        // Value type -> object/ValueType/Enum
        if (typeInfo.ConvertedType.SpecialType is SpecialType.System_Object
            || IsSystemValueTypeOrEnum(typeInfo.ConvertedType))
        {
            var line = expr.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            results.Add(new BoxingOccurrence(methodName,
                $"Boxing: {typeInfo.Type.Name} -> {typeInfo.ConvertedType.Name}", line));
            return;
        }

        // Struct -> interface
        if (typeInfo.ConvertedType.TypeKind == TypeKind.Interface)
        {
            var line = expr.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            results.Add(new BoxingOccurrence(methodName,
                $"Boxing: {typeInfo.Type.Name} -> {typeInfo.ConvertedType.Name} (interface conversion)", line));
        }
    }

    static bool IsSystemValueTypeOrEnum(ITypeSymbol type)
    {
        var fullName = type.OriginalDefinition.ToDisplayString();
        return fullName is "System.ValueType" or "System.Enum";
    }

    static void CheckInterpolationBoxing(InterpolationSyntax interpolation, string methodName,
        SemanticModel model, List<BoxingOccurrence> results)
    {
        var expr = interpolation.Expression;
        var typeInfo = model.GetTypeInfo(expr);
        if (typeInfo.Type is null || !typeInfo.Type.IsValueType)
            return;

        // Check if the value type has overridden ToString - if so, modern compilers
        // may optimize this away, but we still report it as potential boxing
        var line = expr.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        results.Add(new BoxingOccurrence(methodName,
            $"Boxing: {typeInfo.Type.Name} in string interpolation", line));
    }

    static void CheckVirtualCallOnStruct(InvocationExpressionSyntax invocation, string methodName,
        SemanticModel model, List<BoxingOccurrence> results)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var calledMethodName = memberAccess.Name.Identifier.Text;
        if (!VirtualObjectMethods.Contains(calledMethodName))
            return;

        var receiverTypeInfo = model.GetTypeInfo(memberAccess.Expression);
        if (receiverTypeInfo.Type is null || !receiverTypeInfo.Type.IsValueType)
            return;

        // Check if the struct overrides this method
        var structType = receiverTypeInfo.Type;
        var members = structType.GetMembers(calledMethodName);
        var hasOverride = members.OfType<IMethodSymbol>()
            .Any(m => m.IsOverride && SymbolEqualityComparer.Default.Equals(m.ContainingType, structType));

        if (!hasOverride)
        {
            var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            results.Add(new BoxingOccurrence(methodName,
                $"Boxing: virtual call {calledMethodName}() on {structType.Name} (no override)", line));
        }
    }
}
