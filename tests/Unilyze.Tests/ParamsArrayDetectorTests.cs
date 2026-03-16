using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests;

public class ParamsArrayDetectorTests
{
    static IReadOnlyList<ParamsAllocation> Detect(string code, string typeName = "C")
    {
        var model = RoslynTestHelper.CreateSemanticModel(code);
        var typeDecl = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == typeName);
        return ParamsArrayDetector.Detect(typeDecl, model);
    }

    [Fact]
    public void ParamsCallWithIndividualArgs_Detected()
    {
        var code = """
            class Helper {
                public static void Log(params object[] args) { }
            }
            class C {
                void Foo() {
                    Helper.Log("a", "b", "c");
                }
            }
            """;
        var results = Detect(code);
        Assert.Single(results);
        Assert.Equal(3, results[0].ArgCount);
    }

    [Fact]
    public void ParamsCallWithArrayArg_NotDetected()
    {
        var code = """
            class Helper {
                public static void Log(params object[] args) { }
            }
            class C {
                void Foo() {
                    var arr = new object[] { "a", "b" };
                    Helper.Log(arr);
                }
            }
            """;
        var results = Detect(code);
        Assert.Empty(results);
    }

    [Fact]
    public void ParamsCallWithZeroArgs_Detected()
    {
        var code = """
            class Helper {
                public static void Log(params object[] args) { }
            }
            class C {
                void Foo() {
                    Helper.Log();
                }
            }
            """;
        var results = Detect(code);
        Assert.Single(results);
        Assert.Equal(0, results[0].ArgCount);
    }

    [Fact]
    public void NonParamsMethod_NotDetected()
    {
        var code = """
            class Helper {
                public static void Log(string msg) { }
            }
            class C {
                void Foo() {
                    Helper.Log("hello");
                }
            }
            """;
        var results = Detect(code);
        Assert.Empty(results);
    }

    [Fact]
    public void SemanticModelNull_ReturnsEmpty()
    {
        var typeDecl = RoslynTestHelper.GetType(
            "class C { void Foo() { } }", "C");
        var results = ParamsArrayDetector.Detect(typeDecl, model: null);
        Assert.Empty(results);
    }

    [Fact]
    public void ParamsWithFixedArgs_DetectsOnlyParamsExpansion()
    {
        var code = """
            class Helper {
                public static void Log(string fmt, params object[] args) { }
            }
            class C {
                void Foo() {
                    Helper.Log("format", 1, 2);
                }
            }
            """;
        var results = Detect(code);
        Assert.Single(results);
        Assert.Equal(2, results[0].ArgCount);
    }
}
