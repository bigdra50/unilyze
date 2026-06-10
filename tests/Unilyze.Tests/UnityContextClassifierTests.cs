using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests;

public class UnityContextClassifierTests
{
    const string UnityStub = """
        namespace UnityEngine
        {
            public class MonoBehaviour { }
        }
        """;

    static (TypeDeclarationSyntax TypeDecl, SemanticModel? Model) Parse(
        string code,
        string typeName = "Player",
        bool semantic = true)
    {
        var fullCode = semantic ? UnityStub + code : code;
        if (semantic)
        {
            var model = RoslynTestHelper.CreateSemanticModel(fullCode);
            var typeDecl = model.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .First(td => td.Identifier.Text == typeName);
            return (typeDecl, model);
        }

        var typeDeclOnly = RoslynTestHelper.GetType(fullCode, typeName);
        return (typeDeclOnly, null);
    }

    [Fact]
    public void Classify_DirectMonoBehaviourDerivation_IsMonoBehaviour()
    {
        var (typeDecl, model) = Parse("""
            class Player : UnityEngine.MonoBehaviour { }
            """);

        var context = UnityContextClassifier.Classify(typeDecl, model);

        Assert.True(context.IsMonoBehaviour);
    }

    [Fact]
    public void Classify_IndirectMonoBehaviourDerivation_SemanticOnly()
    {
        var (typeDecl, model) = Parse("""
            class BaseView : UnityEngine.MonoBehaviour { }
            class Player : BaseView { }
            """);

        var context = UnityContextClassifier.Classify(typeDecl, model);

        Assert.True(context.IsMonoBehaviour);
    }

    [Fact]
    public void Classify_SyntaxFallback_DirectBaseMatchesMonoBehaviour()
    {
        var (typeDecl, model) = Parse("""
            class Player : MonoBehaviour { }
            """, semantic: false);

        var context = UnityContextClassifier.Classify(typeDecl, model);

        Assert.True(context.IsMonoBehaviour);
    }

    [Fact]
    public void Classify_SyntaxFallback_IntermediateBaseDoesNotMatch()
    {
        var (typeDecl, model) = Parse("""
            class BaseView : MonoBehaviour { }
            class Player : BaseView { }
            """, semantic: false);

        var context = UnityContextClassifier.Classify(typeDecl, model);

        Assert.False(context.IsMonoBehaviour);
        Assert.Empty(context.HotPathMethodNames);
    }

    [Fact]
    public void Classify_NonMonoBehaviourClass_ReturnsFalse()
    {
        var (typeDecl, model) = Parse("""
            class PlainClass { }
            """, typeName: "PlainClass");

        var context = UnityContextClassifier.Classify(typeDecl, model);

        Assert.False(context.IsMonoBehaviour);
        Assert.Empty(context.HotPathMethodNames);
    }

    [Fact]
    public void Classify_CoroutineMethod_IsHotPath()
    {
        var (typeDecl, model) = Parse("""
            using System.Collections;
            class Player : UnityEngine.MonoBehaviour
            {
                IEnumerator MyCoroutine() { yield break; }
            }
            """);

        var context = UnityContextClassifier.Classify(typeDecl, model);

        Assert.True(context.IsMonoBehaviour);
        Assert.Contains("MyCoroutine", context.HotPathMethodNames);
    }

    [Fact]
    public void Classify_LifecycleMethods_AreNotHotPath()
    {
        var (typeDecl, model) = Parse("""
            class Player : UnityEngine.MonoBehaviour
            {
                void Awake() { }
                void Start() { }
                void Update() { }
            }
            """);

        var context = UnityContextClassifier.Classify(typeDecl, model);

        Assert.Contains("Update", context.HotPathMethodNames);
        Assert.DoesNotContain("Awake", context.HotPathMethodNames);
        Assert.DoesNotContain("Start", context.HotPathMethodNames);
    }

    [Fact]
    public void Classify_AllBuiltInHotPathMethods_AreCollected()
    {
        var (typeDecl, model) = Parse("""
            class Player : UnityEngine.MonoBehaviour
            {
                void Update() { }
                void FixedUpdate() { }
                void LateUpdate() { }
                void OnGUI() { }
            }
            """);

        var context = UnityContextClassifier.Classify(typeDecl, model);

        Assert.Contains("Update", context.HotPathMethodNames);
        Assert.Contains("FixedUpdate", context.HotPathMethodNames);
        Assert.Contains("LateUpdate", context.HotPathMethodNames);
        Assert.Contains("OnGUI", context.HotPathMethodNames);
    }
}
