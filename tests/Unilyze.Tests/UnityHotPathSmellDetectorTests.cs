using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Unilyze.Tests;

public class UnityHotPathSmellDetectorTests
{
    const string UnityStub = """
        namespace UnityEngine
        {
            public class MonoBehaviour { }
            public class Component { }
            public class Camera { public static Camera main; }
            public class Object
            {
                public static T FindObjectOfType<T>() => default;
            }
        }
        """;

    static readonly ISmellDetector[] Detectors =
    [
        new ExpensiveUnityApiInHotPathDetector(),
        new LinqInHotPathDetector(),
        new CollectionAllocationInHotPathDetector(),
        new StringConcatenationInHotPathDetector(),
    ];

    static (TypeDeclarationSyntax TypeDecl, SemanticModel? Model) Parse(
        string code,
        string typeName = "Player",
        bool semantic = true)
    {
        var fullCode = semantic ? UnityStub + code : code;
        if (semantic)
        {
            var tree = RoslynTestHelper.ParseCode(fullCode);
            var compilation = CSharpCompilation.Create(
                "Test",
                [tree],
                [
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                ],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var typeDecl = tree.GetRoot()
                .DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .First(td => td.Identifier.Text == typeName);
            return (typeDecl, model);
        }

        var typeDeclOnly = RoslynTestHelper.GetType(fullCode, typeName);
        return (typeDeclOnly, null);
    }

    static List<DetectedSmell> DetectSmells(
        string code,
        string typeName = "Player",
        bool semantic = true)
    {
        var (typeDecl, model) = Parse(code, typeName, semantic);
        return Detectors.SelectMany(d => d.Detect(typeDecl, model)).ToList();
    }

    static List<DetectedSmell> Filter(List<DetectedSmell> smells, CodeSmellKind kind) =>
        smells.Where(s => s.Kind == kind).ToList();

    [Fact]
    public void Detect_GetComponentInUpdate_ReportsExpensiveUnityApi()
    {
        var smells = Filter(DetectSmells("""
            class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    GetComponent<UnityEngine.Component>();
                }
            }
            """), CodeSmellKind.ExpensiveUnityApiInHotPath);

        Assert.Single(smells);
        Assert.Equal("Update", smells[0].MethodName);
        Assert.Equal(SmellSeverity.Warning, smells[0].Severity);
        Assert.True(smells[0].Line > 0);
    }

    [Fact]
    public void Detect_GetComponentInChildrenInUpdate_ReportsExpensiveUnityApi()
    {
        var smells = Filter(DetectSmells("""
            class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    GetComponentInChildren<UnityEngine.Component>();
                }
            }
            """), CodeSmellKind.ExpensiveUnityApiInHotPath);

        Assert.Single(smells);
        Assert.Equal("Update", smells[0].MethodName);
    }

    [Fact]
    public void Detect_FindObjectOfTypeInUpdate_ReportsExpensiveUnityApi()
    {
        var smells = Filter(DetectSmells("""
            class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    UnityEngine.Object.FindObjectOfType<UnityEngine.Component>();
                }
            }
            """), CodeSmellKind.ExpensiveUnityApiInHotPath);

        Assert.Single(smells);
        Assert.Equal("Update", smells[0].MethodName);
    }

    [Fact]
    public void Detect_CameraMainInUpdate_ReportsExpensiveUnityApi()
    {
        var smells = Filter(DetectSmells("""
            class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    var cam = UnityEngine.Camera.main;
                }
            }
            """), CodeSmellKind.ExpensiveUnityApiInHotPath);

        Assert.Single(smells);
        Assert.Equal("Update", smells[0].MethodName);
    }

    [Fact]
    public void Detect_LinqInUpdate_ReportsLinqInHotPath()
    {
        var smells = Filter(DetectSmells("""
            using System.Linq;
            class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    var xs = new[] { 1, 2 }.Where(x => x > 0).ToList();
                }
            }
            """), CodeSmellKind.LinqInHotPath);

        Assert.NotEmpty(smells);
        Assert.All(smells, s => Assert.Equal("Update", s.MethodName));
        Assert.True(smells[0].Line > 0);
    }

    [Fact]
    public void Detect_NewListInUpdate_ReportsCollectionAllocation()
    {
        var smells = Filter(DetectSmells("""
            using System.Collections.Generic;
            class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    var list = new List<int>();
                }
            }
            """), CodeSmellKind.CollectionAllocationInHotPath);

        Assert.Single(smells);
        Assert.Equal("Update", smells[0].MethodName);
        Assert.True(smells[0].Line > 0);
    }

    [Fact]
    public void Detect_ArrayCreationInUpdate_ReportsCollectionAllocation()
    {
        var smells = Filter(DetectSmells("""
            class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    var arr = new int[3];
                }
            }
            """), CodeSmellKind.CollectionAllocationInHotPath);

        Assert.Single(smells);
        Assert.Equal("Update", smells[0].MethodName);
    }

    [Fact]
    public void Detect_CollectionExpressionInUpdate_ReportsCollectionAllocation()
    {
        var smells = Filter(DetectSmells("""
            class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    int[] arr = [1, 2];
                }
            }
            """), CodeSmellKind.CollectionAllocationInHotPath);

        Assert.Single(smells);
        Assert.Equal("Update", smells[0].MethodName);
    }

    [Fact]
    public void Detect_StringConcatInUpdate_ReportsStringConcatenation()
    {
        var smells = Filter(DetectSmells("""
            class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    var s = "a" + "b";
                }
            }
            """), CodeSmellKind.StringConcatenationInHotPath);

        Assert.Single(smells);
        Assert.Equal("Update", smells[0].MethodName);
        Assert.True(smells[0].Line > 0);
    }

    [Fact]
    public void Detect_InterpolatedStringInUpdate_ReportsStringConcatenation()
    {
        var smells = Filter(DetectSmells("""
            class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    var s = $"value {1}";
                }
            }
            """), CodeSmellKind.StringConcatenationInHotPath);

        Assert.Single(smells);
        Assert.Equal("Update", smells[0].MethodName);
    }

    [Fact]
    public void Detect_StringFormatInUpdate_ReportsStringConcatenation()
    {
        var smells = Filter(DetectSmells("""
            class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    var s = string.Format("{0}", 1);
                }
            }
            """), CodeSmellKind.StringConcatenationInHotPath);

        Assert.Single(smells);
        Assert.Equal("Update", smells[0].MethodName);
    }

    [Fact]
    public void Detect_GetComponentInAwake_DoesNotReport()
    {
        var smells = Filter(DetectSmells("""
            class Player : UnityEngine.MonoBehaviour
            {
                void Awake()
                {
                    GetComponent<UnityEngine.Component>();
                }
            }
            """), CodeSmellKind.ExpensiveUnityApiInHotPath);

        Assert.Empty(smells);
    }

    [Fact]
    public void Detect_GetComponentInStart_DoesNotReport()
    {
        var smells = Filter(DetectSmells("""
            class Player : UnityEngine.MonoBehaviour
            {
                void Start()
                {
                    GetComponent<UnityEngine.Component>();
                }
            }
            """), CodeSmellKind.ExpensiveUnityApiInHotPath);

        Assert.Empty(smells);
    }

    [Fact]
    public void Detect_GetComponentInPlainClass_DoesNotReport()
    {
        var smells = Filter(DetectSmells("""
            class PlainClass
            {
                void Update()
                {
                    GetComponent<object>();
                }

                void GetComponent<T>() { }
            }
            """, typeName: "PlainClass"), CodeSmellKind.ExpensiveUnityApiInHotPath);

        Assert.Empty(smells);
    }

    [Fact]
    public void Detect_GetComponentOnNonUnityType_DoesNotReport()
    {
        var smells = Filter(DetectSmells("""
            class Helper
            {
                public void GetComponent<T>() { }
            }

            class Player : UnityEngine.MonoBehaviour
            {
                void Update()
                {
                    new Helper().GetComponent<int>();
                }
            }
            """), CodeSmellKind.ExpensiveUnityApiInHotPath);

        Assert.Empty(smells);
    }

    [Fact]
    public void Detect_SyntaxOnly_GetComponentInUpdate_ReportsByName()
    {
        var smells = Filter(DetectSmells("""
            class MonoBehaviour { }
            class Player : MonoBehaviour
            {
                void Update()
                {
                    GetComponent<object>();
                }

                void GetComponent<T>() { }
            }
            """, semantic: false), CodeSmellKind.ExpensiveUnityApiInHotPath);

        Assert.Single(smells);
        Assert.Equal("Update", smells[0].MethodName);
    }
}
