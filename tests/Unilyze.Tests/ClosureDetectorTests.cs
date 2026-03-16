using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests;

public class ClosureDetectorTests
{
    static IReadOnlyList<ClosureCapture> DetectSemantic(string code, string typeName = "C")
    {
        var model = RoslynTestHelper.CreateSemanticModel(code);
        var typeDecl = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == typeName);
        return ClosureDetector.Detect(typeDecl, model);
    }

    static IReadOnlyList<ClosureCapture> DetectSyntactic(string code, string typeName = "C")
    {
        var typeDecl = RoslynTestHelper.GetType(code, typeName);
        return ClosureDetector.Detect(typeDecl, model: null);
    }

    [Fact]
    public void NoCapturedVariables_ReturnsEmpty()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    Action<int> a = x => Console.WriteLine(x);
                }
            }
            """;
        var results = DetectSemantic(code);
        Assert.Empty(results);
    }

    [Fact]
    public void CapturesOuterVariable_Detected()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    int count = 0;
                    Action a = () => Console.WriteLine(count);
                }
            }
            """;
        var results = DetectSemantic(code);
        Assert.Single(results);
        Assert.Contains("count", results[0].CapturedVariables);
    }

    [Fact]
    public void CapturesThis_WhenAccessingInstanceMember()
    {
        var code = """
            using System;
            class C {
                int _field;
                void Foo() {
                    Action a = () => Console.WriteLine(_field);
                }
            }
            """;
        var results = DetectSemantic(code);
        Assert.Single(results);
        Assert.Contains("this", results[0].CapturedVariables);
    }

    [Fact]
    public void LambdaUsesOnlyItsOwnParameters_ReturnsEmpty()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    Func<int, int> f = x => x * 2;
                }
            }
            """;
        var results = DetectSemantic(code);
        Assert.Empty(results);
    }

    [Fact]
    public void SyntacticFallback_CapturesParameter()
    {
        var code = """
            using System;
            class C {
                void Foo(int count) {
                    Action a = () => Console.WriteLine(count);
                }
            }
            """;
        var results = DetectSyntactic(code);
        Assert.Single(results);
        Assert.Contains("count", results[0].CapturedVariables);
    }

    [Fact]
    public void SyntacticFallback_NoCapturedVariables_ReturnsEmpty()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    Func<int, int> f = x => x * 2;
                }
            }
            """;
        var results = DetectSyntactic(code);
        Assert.Empty(results);
    }
}
