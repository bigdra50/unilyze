using Unilyze.Unity;
using Unilyze.Detectors;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Pipeline;

internal static class TypeRoleStamper
{
    public static IReadOnlyList<TypeNodeInfo> ApplyRoles(
        IReadOnlyList<TypeNodeInfo> types,
        IReadOnlyList<SyntaxTree> syntaxTrees,
        CompilationResult compilationResult)
    {
        if (types.Count == 0)
            return types;

        var treeByPath = SyntaxLookups.BuildTreeLookup(compilationResult, syntaxTrees);
        var typeDeclLookup = SyntaxLookups.BuildTypeDeclLookup(types, treeByPath);
        var modelCache = new ConcurrentDictionary<SyntaxTree, SemanticModel>();

        var stamped = new TypeNodeInfo[types.Count];
        Parallel.For(0, types.Count, i =>
        {
            var type = types[i];
            var key = TypeIdentity.GetTypeId(type);
            typeDeclLookup.TryGetValue(key, out var typeDecl);
            SemanticModel? model = null;
            if (typeDecl is not null && compilationResult.Compilation is not null)
            {
                model = modelCache.GetOrAdd(
                    typeDecl.SyntaxTree,
                    static (t, c) => c.GetSemanticModel(t),
                    compilationResult.Compilation);
            }

            var role = EcsContextClassifier.ClassifyEcsRole(type, typeDecl, model)
                ?? UnityContextClassifier.ClassifyRole(type, typeDecl, model);
            stamped[i] = type with { Role = role };
        });

        return stamped;
    }
}
