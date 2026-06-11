using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze;

public static class EcsContextClassifier
{
    public static TypeRole? ClassifyEcsRole(TypeDeclarationSyntax typeDecl, SemanticModel? model)
    {
        if (EcsInterfaceMatcher.IsEcsSystem(typeDecl, model))
            return TypeRole.EcsSystem;
        if (EcsInterfaceMatcher.IsEcsJob(typeDecl, model))
            return TypeRole.EcsJob;
        if (EcsInterfaceMatcher.IsEcsComponentData(typeDecl, model))
            return TypeRole.EcsComponentData;
        return null;
    }

    public static TypeRole? ClassifyEcsRole(TypeNodeInfo typeInfo, TypeDeclarationSyntax? typeDecl, SemanticModel? model)
    {
        if (typeDecl is not null)
            return ClassifyEcsRole(typeDecl, model);

        if (typeInfo.Kind is not ("struct" or "record struct"))
            return null;

        if (EcsInterfaceMatcher.ImplementsInterfaceName(typeInfo.Interfaces, "ISystem"))
            return TypeRole.EcsSystem;
        if (EcsInterfaceMatcher.ImplementsJobInterfaceName(typeInfo.Interfaces))
            return TypeRole.EcsJob;
        if (EcsInterfaceMatcher.ImplementsInterfaceName(typeInfo.Interfaces, "IComponentData"))
            return TypeRole.EcsComponentData;
        return null;
    }

    public static bool IsEcsSystem(TypeDeclarationSyntax typeDecl, SemanticModel? model)
        => EcsInterfaceMatcher.IsEcsSystem(typeDecl, model);

    public static bool IsEcsJob(TypeDeclarationSyntax typeDecl, SemanticModel? model)
        => EcsInterfaceMatcher.IsEcsJob(typeDecl, model);

    public static bool IsEcsComponentData(TypeDeclarationSyntax typeDecl, SemanticModel? model)
        => EcsInterfaceMatcher.IsEcsComponentData(typeDecl, model);

    public static bool IsBurstEligible(TypeRole role, string kind)
        => kind is "struct" or "record struct"
           && role is TypeRole.EcsSystem or TypeRole.EcsJob;
}
