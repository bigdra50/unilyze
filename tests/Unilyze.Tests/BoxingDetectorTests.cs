using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests;

public class BoxingDetectorTests
{
    static IReadOnlyList<BoxingOccurrence> Detect(string code, string typeName = "C")
    {
        var model = RoslynTestHelper.CreateSemanticModel(code);
        var typeDecl = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == typeName);
        return BoxingDetector.Detect(typeDecl, model);
    }

    [Fact]
    public void EmptyClass_ReturnsEmpty()
    {
        var results = Detect("class C { }");
        Assert.Empty(results);
    }

    [Fact]
    public void ValueTypeToObject_DetectsBoxing()
    {
        var code = """
            class C {
                void Foo() {
                    object o = 42;
                }
            }
            """;
        var results = Detect(code);
        Assert.Contains(results, r => r.MethodName == "Foo" && r.Description.Contains("Boxing"));
    }

    [Fact]
    public void StructToInterface_DetectsBoxing()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    IComparable c = 42;
                }
            }
            """;
        var results = Detect(code);
        Assert.Contains(results, r => r.Description.Contains("interface conversion"));
    }

    [Fact]
    public void StructWithOverriddenToString_NoBoxingOnCall()
    {
        var code = """
            struct S {
                public override string ToString() => "s";
            }
            class C {
                void Foo() {
                    var s = new S();
                    var str = s.ToString();
                }
            }
            """;
        var results = Detect(code);
        Assert.DoesNotContain(results,
            r => r.Description.Contains("virtual call ToString()"));
    }

    [Fact]
    public void StructWithoutOverride_GetHashCode_DetectsBoxing()
    {
        var code = """
            struct S {
                public int X;
            }
            class C {
                void Foo() {
                    var s = new S();
                    var h = s.GetHashCode();
                }
            }
            """;
        var results = Detect(code);
        Assert.Contains(results,
            r => r.Description.Contains("virtual call GetHashCode()") && r.Description.Contains("no override"));
    }

    [Fact]
    public void SemanticModelNull_ReturnsEmpty()
    {
        var typeDecl = RoslynTestHelper.GetType("class C { void Foo() { object o = 42; } }", "C");
        var results = BoxingDetector.Detect(typeDecl, model: null);
        Assert.Empty(results);
    }

    [Fact]
    public void StringInterpolation_ValueType_DetectsBoxing()
    {
        var code = """
            class C {
                void Foo() {
                    int x = 42;
                    var s = $"value is {x}";
                }
            }
            """;
        var results = Detect(code);
        Assert.Contains(results, r => r.Description.Contains("string interpolation"));
    }
}
