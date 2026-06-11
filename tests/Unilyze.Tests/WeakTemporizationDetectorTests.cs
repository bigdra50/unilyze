using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests;

public class WeakTemporizationDetectorTests
{
    const string UnityStub = """
        namespace UnityEngine
        {
            public class MonoBehaviour { public Transform transform = new(); }
            public struct Vector3
            {
                public static Vector3 zero;
                public static Vector3 operator +(Vector3 a, Vector3 b) => default;
                public static Vector3 operator *(Vector3 a, float b) => default;
            }
            public class Transform
            {
                public Vector3 position;
                public void Translate(Vector3 translation) { }
                public void Rotate(Vector3 eulers) { }
                public void RotateAround(Vector3 point, Vector3 axis, float angle) { }
            }
            public static class Time
            {
                public static float deltaTime;
                public static float time;
            }
            public static class Mathf
            {
                public static float Sin(float f) => f;
            }
        }
        """;

    const string NonUnityTransformStub = """
        class CustomTransform
        {
            public Vector3 position;
        }
        struct Vector3
        {
            public static Vector3 zero;
            public static Vector3 operator +(Vector3 a, Vector3 b) => default;
            public static Vector3 operator *(Vector3 a, float b) => default;
        }
        """;

    static (TypeDeclarationSyntax TypeDecl, Microsoft.CodeAnalysis.SemanticModel? Model) Parse(
        string code, string typeName, bool semantic)
    {
        var fullCode = UnityStub + code;
        if (semantic)
        {
            var model = RoslynTestHelper.CreateSemanticModel(fullCode);
            var typeDecl = model.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .First(td => td.Identifier.Text == typeName);
            return (typeDecl, model);
        }

        return (RoslynTestHelper.GetType(fullCode, typeName), null);
    }

    static IReadOnlyList<WeakTemporizationFinding> Analyze(string code, string typeName = "Player", bool semantic = false)
    {
        var (typeDecl, model) = Parse(code, typeName, semantic);
        return WeakTemporizationAnalyzer.Analyze(typeDecl, model);
    }

    static IReadOnlyList<DetectedSmell> DetectSmells(string code, string typeName = "Player", bool semantic = false)
    {
        var (typeDecl, model) = Parse(code, typeName, semantic);
        return new WeakTemporizationSmellDetector().Detect(typeDecl, model);
    }

    [Fact]
    public void Update_CompoundAssign_Unscaled_Flags()
    {
        var findings = Analyze("""
            class Player : UnityEngine.MonoBehaviour
            {
                Vector3 velocity;
                void Update()
                {
                    transform.position += velocity;
                }
            }
            """);

        Assert.Contains(findings, f => f.MethodName == "Update");
    }

    [Fact]
    public void LateUpdate_CompoundAssign_Unscaled_Flags()
    {
        var findings = Analyze("""
            class Player : UnityEngine.MonoBehaviour
            {
                Vector3 velocity;
                void LateUpdate()
                {
                    transform.position += velocity;
                }
            }
            """);

        Assert.Contains(findings, f => f.MethodName == "LateUpdate");
    }

    [Fact]
    public void Update_IncrementalSimpleAssign_Unscaled_Flags()
    {
        var findings = Analyze("""
            class Player : UnityEngine.MonoBehaviour
            {
                Vector3 delta;
                void Update()
                {
                    transform.position = transform.position + delta;
                }
            }
            """);

        Assert.Contains(findings, f => f.MethodName == "Update");
    }

    [Fact]
    public void Update_Translate_Unscaled_Flags()
    {
        var findings = Analyze("""
            class Player : UnityEngine.MonoBehaviour
            {
                Vector3 dir;
                float speed;
                void Update()
                {
                    transform.Translate(dir * speed);
                }
            }
            """);

        Assert.Contains(findings, f => f.MethodName == "Update");
    }

    [Fact]
    public void Update_DirectDeltaTimeScaling_DoesNotFlag()
    {
        var findings = Analyze("""
            class Player : UnityEngine.MonoBehaviour
            {
                Vector3 velocity;
                void Update()
                {
                    transform.position += velocity * UnityEngine.Time.deltaTime;
                }
            }
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void Update_LocalStepPropagation_DoesNotFlag()
    {
        var findings = Analyze("""
            class Player : UnityEngine.MonoBehaviour
            {
                Vector3 velocity;
                float speed;
                void Update()
                {
                    var step = speed * UnityEngine.Time.deltaTime;
                    transform.position += step;
                }
            }
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void Update_TranslateWithDeltaTime_DoesNotFlag()
    {
        var findings = Analyze("""
            class Player : UnityEngine.MonoBehaviour
            {
                Vector3 dir;
                float speed;
                void Update()
                {
                    transform.Translate(dir * speed * UnityEngine.Time.deltaTime);
                }
            }
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void NonMonoBehaviour_DoesNotFlag()
    {
        var findings = Analyze("""
            class Player
            {
                UnityEngine.Transform transform = new();
                Vector3 velocity;
                void Update()
                {
                    transform.position += velocity;
                }
            }
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void FixedUpdate_Unscaled_DoesNotFlag()
    {
        var findings = Analyze("""
            class Player : UnityEngine.MonoBehaviour
            {
                Vector3 velocity;
                void FixedUpdate()
                {
                    transform.position += velocity;
                }
            }
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void RegularMethod_Unscaled_DoesNotFlag()
    {
        var findings = Analyze("""
            class Player : UnityEngine.MonoBehaviour
            {
                Vector3 velocity;
                void Move()
                {
                    transform.position += velocity;
                }
            }
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void Update_AbsoluteAssign_DoesNotFlag()
    {
        var findings = Analyze("""
            class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    transform.position = UnityEngine.Vector3.zero;
                }
            }
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void Update_SinTime_DoesNotFlag()
    {
        var findings = Analyze("""
            class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    transform.position += new UnityEngine.Vector3(UnityEngine.Mathf.Sin(UnityEngine.Time.time), 0, 0);
                }
            }
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void SyntaxOnly_DetectsViolation()
    {
        var smells = DetectSmells("""
            class Player : UnityEngine.MonoBehaviour
            {
                Vector3 velocity;
                void Update()
                {
                    transform.position += velocity;
                }
            }
            """, semantic: false);

        var smell = Assert.Single(smells);
        Assert.Equal(CodeSmellKind.WeakTemporization, smell.Kind);
        Assert.Equal(SmellSeverity.Warning, smell.Severity);
    }

    [Fact]
    public void Semantic_UserDefinedTransform_Suppresses()
    {
        var code = UnityStub + NonUnityTransformStub + """
            class Player : UnityEngine.MonoBehaviour
            {
                CustomTransform transform = new();
                Vector3 velocity;
                void Update()
                {
                    transform.position += velocity;
                }
            }
            """;

        var model = RoslynTestHelper.CreateSemanticModel(code);
        var typeDecl = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == "Player");

        var findings = WeakTemporizationAnalyzer.Analyze(typeDecl, model);
        Assert.Empty(findings);
    }
}
