using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Pipeline;

internal static class MemberIdentity
{
    public static string CreateMethodId(string typeId, MethodDeclarationSyntax method)
    {
        var explicitInterface = method.ExplicitInterfaceSpecifier?.Name.ToString();
        var name = method.Identifier.Text;
        var arity = method.TypeParameterList?.Parameters.Count ?? 0;
        var paramSig = FormatParameterSignature(method.ParameterList);
        var sig = FormatMethodSignature(explicitInterface, name, arity, paramSig);
        return $"{typeId}|Method:{sig}";
    }

    public static string CreateConstructorId(string typeId, ConstructorDeclarationSyntax ctor)
    {
        var paramSig = FormatParameterSignature(ctor.ParameterList);
        return $"{typeId}|Ctor:.ctor({paramSig})";
    }

    public static string CreateDestructorId(string typeId)
        => $"{typeId}|Dtor:.dtor()";

    public static string CreateOperatorId(string typeId, OperatorDeclarationSyntax op)
    {
        var name = MemberBodyEnumerator.GetOperatorMemberName(op);
        var checkedPrefix = op.Modifiers.Any(m => m.IsKind(SyntaxKind.CheckedKeyword)) ? "checked_" : "";
        var paramSig = FormatParameterSignature(op.ParameterList);
        return $"{typeId}|Operator:{checkedPrefix}{name}({paramSig})";
    }

    public static string CreateConversionOperatorId(string typeId, ConversionOperatorDeclarationSyntax conv)
    {
        var kind = conv.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ImplicitKeyword)
            ? "op_Implicit"
            : "op_Explicit";
        var checkedPrefix = conv.Modifiers.Any(m => m.IsKind(SyntaxKind.CheckedKeyword)) ? "checked_" : "";
        var targetType = conv.Type.ToString();
        var paramSig = FormatParameterSignature(conv.ParameterList);
        return $"{typeId}|ConvOp:{checkedPrefix}{kind}->{targetType}({paramSig})";
    }

    public static string CreatePropertyAccessorId(string typeId, string propertyName, AccessorDeclarationSyntax accessor)
    {
        var prefix = GetAccessorKindName(accessor);
        return $"{typeId}|Accessor:{prefix}_{propertyName}";
    }

    public static string CreateIndexerAccessorId(
        string typeId, IndexerDeclarationSyntax indexer, AccessorDeclarationSyntax accessor)
    {
        var prefix = GetAccessorKindName(accessor);
        var paramSig = FormatParameterSignature(indexer.ParameterList);
        return $"{typeId}|Accessor:{prefix}_Item({paramSig})";
    }

    public static string CreateEventAccessorId(string typeId, string eventName, AccessorDeclarationSyntax accessor)
    {
        var kind = accessor.Kind() switch
        {
            SyntaxKind.AddAccessorDeclaration => "add",
            SyntaxKind.RemoveAccessorDeclaration => "remove",
            _ => "accessor"
        };
        return $"{typeId}|EventAccessor:{kind}_{eventName}";
    }

    public static string CreateFieldInitializerId(string typeId, string fieldName)
        => $"{typeId}|FieldInit:{fieldName}.init";

    public static string CreatePropertyInitializerId(string typeId, string propertyName)
        => $"{typeId}|PropInit:{propertyName}.init";

    public static string CreateLocalFunctionId(string parentMemberId, LocalFunctionStatementSyntax localFunc)
    {
        var name = localFunc.Identifier.Text;
        var arity = localFunc.TypeParameterList?.Parameters.Count ?? 0;
        var paramSig = FormatParameterSignature(localFunc.ParameterList);
        var sig = arity > 0 ? $"{name}`{arity}({paramSig})" : $"{name}({paramSig})";
        return $"{parentMemberId}>{sig}";
    }

    public static string CreateFieldId(string typeId, string fieldName)
        => $"{typeId}|Field:{fieldName}";

    public static string CreatePropertyId(string typeId, string propertyName)
        => $"{typeId}|Property:{propertyName}";

    public static string CreateEventId(string typeId, string eventName)
        => $"{typeId}|Event:{eventName}";

    public static string CreateIndexerId(string typeId, IndexerDeclarationSyntax indexer)
    {
        var paramSig = FormatParameterSignature(indexer.ParameterList);
        return $"{typeId}|Indexer:this[{paramSig}]";
    }

    public static string CreateEnumMemberId(string typeId, string memberName)
        => $"{typeId}|EnumMember:{memberName}";

    static string FormatMethodSignature(string? explicitInterface, string name, int arity, string paramSig)
    {
        var prefix = explicitInterface is not null ? $"{explicitInterface}." : "";
        var genericSuffix = arity > 0 ? $"`{arity}" : "";
        return $"{prefix}{name}{genericSuffix}({paramSig})";
    }

    static string FormatParameterSignature(ParameterListSyntax parameterList)
    {
        if (parameterList.Parameters.Count == 0)
            return "";

        return string.Join(",", parameterList.Parameters.Select(FormatParameter));
    }

    static string FormatParameterSignature(BracketedParameterListSyntax parameterList)
    {
        if (parameterList.Parameters.Count == 0)
            return "";

        return string.Join(",", parameterList.Parameters.Select(FormatParameter));
    }

    static string FormatParameter(ParameterSyntax param)
    {
        var refKind = GetRefKindPrefix(param);
        var type = param.Type?.ToString() ?? "unknown";
        return $"{refKind}{type}";
    }

    static string GetRefKindPrefix(ParameterSyntax param)
    {
        foreach (var modifier in param.Modifiers)
        {
            switch (modifier.Kind())
            {
                case SyntaxKind.RefKeyword: return "ref ";
                case SyntaxKind.OutKeyword: return "out ";
                case SyntaxKind.InKeyword: return "in ";
                case SyntaxKind.ParamsKeyword: return "params ";
                case SyntaxKind.ScopedKeyword: return "scoped ";
            }
        }

        return "";
    }

    static string GetAccessorKindName(AccessorDeclarationSyntax accessor) => accessor.Kind() switch
    {
        SyntaxKind.GetAccessorDeclaration => "get",
        SyntaxKind.SetAccessorDeclaration => "set",
        SyntaxKind.InitAccessorDeclaration => "init",
        _ => "accessor"
    };
}
