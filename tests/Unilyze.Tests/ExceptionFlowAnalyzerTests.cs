using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests;

public class ExceptionFlowAnalyzerTests
{
    static ExceptionFlowResult Analyze(string code, string typeName = "C")
    {
        var typeDecl = RoslynTestHelper.GetType(code, typeName);
        return ExceptionFlowAnalyzer.Analyze(typeDecl, model: null);
    }

    static ExceptionFlowResult AnalyzeSemantic(string code, string typeName = "C")
    {
        var model = RoslynTestHelper.CreateSemanticModel(code);
        var typeDecl = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == typeName);
        return ExceptionFlowAnalyzer.Analyze(typeDecl, model);
    }

    // --- CatchAll tests ---

    [Fact]
    public void CatchAll_WithoutRethrow_Detected()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    try { }
                    catch (Exception) { Console.WriteLine("caught"); }
                }
            }
            """;
        var result = Analyze(code);
        var clause = Assert.Single(result.CatchAllClauses);
        Assert.Equal("Foo", clause.MethodName);
        Assert.False(clause.HasRethrow);
    }

    [Fact]
    public void CatchAll_WithRethrow_NotDetected_AsCatchAll_ButHasRethrowTrue()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    try { }
                    catch (Exception) { throw; }
                }
            }
            """;
        var result = Analyze(code);
        var clause = Assert.Single(result.CatchAllClauses);
        Assert.True(clause.HasRethrow);
    }

    [Fact]
    public void CatchSpecific_NotCatchAll()
    {
        var code = """
            using System;
            using System.IO;
            class C {
                void Foo() {
                    try { }
                    catch (IOException) { }
                }
            }
            """;
        var result = Analyze(code);
        Assert.Empty(result.CatchAllClauses);
    }

    [Fact]
    public void BareCatch_Detected()
    {
        var code = """
            class C {
                void Foo() {
                    try { }
                    catch { }
                }
            }
            """;
        var result = Analyze(code);
        var clause = Assert.Single(result.CatchAllClauses);
        Assert.Equal("Foo", clause.MethodName);
        Assert.False(clause.HasRethrow);
    }

    [Fact]
    public void CatchAll_WithNewException_NotRethrow()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    try { }
                    catch (Exception) { throw new InvalidOperationException(); }
                }
            }
            """;
        var result = Analyze(code);
        var clause = Assert.Single(result.CatchAllClauses);
        Assert.False(clause.HasRethrow);
    }

    // --- MissingInnerException tests ---

    [Fact]
    public void MissingInner_WithoutCatchVar_Detected()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    try { }
                    catch (Exception e) { throw new InvalidOperationException("msg"); }
                }
            }
            """;
        var result = Analyze(code);
        var missing = Assert.Single(result.MissingInnerExceptions);
        Assert.Equal("Foo", missing.MethodName);
        Assert.Equal("InvalidOperationException", missing.NewExceptionType);
    }

    [Fact]
    public void MissingInner_WithCatchVarAsInner_NotDetected()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    try { }
                    catch (Exception e) { throw new InvalidOperationException("msg", e); }
                }
            }
            """;
        var result = Analyze(code);
        Assert.Empty(result.MissingInnerExceptions);
    }

    [Fact]
    public void MissingInner_Rethrow_NotDetected()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    try { }
                    catch (Exception) { throw; }
                }
            }
            """;
        var result = Analyze(code);
        Assert.Empty(result.MissingInnerExceptions);
    }

    // --- SystemExceptionThrow tests ---

    [Fact]
    public void SystemException_DirectThrow_Detected()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    throw new Exception("error");
                }
            }
            """;
        var result = Analyze(code);
        var sysEx = Assert.Single(result.SystemExceptionThrows);
        Assert.Equal("Foo", sysEx.MethodName);
    }

    [Fact]
    public void ArgumentNullException_NotDetected()
    {
        var code = """
            using System;
            class C {
                void Foo(string s) {
                    throw new ArgumentNullException("s");
                }
            }
            """;
        var result = Analyze(code);
        Assert.Empty(result.SystemExceptionThrows);
    }

    [Fact]
    public void InvalidOperationException_NotDetected()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    throw new InvalidOperationException();
                }
            }
            """;
        var result = Analyze(code);
        Assert.Empty(result.SystemExceptionThrows);
    }

    // --- SemanticModel tests ---

    [Fact]
    public void Semantic_CatchAll_Detected()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    try { }
                    catch (Exception) { }
                }
            }
            """;
        var result = AnalyzeSemantic(code);
        var clause = Assert.Single(result.CatchAllClauses);
        Assert.Equal("Foo", clause.MethodName);
    }

    [Fact]
    public void Semantic_SystemException_Detected()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    throw new Exception("error");
                }
            }
            """;
        var result = AnalyzeSemantic(code);
        Assert.Single(result.SystemExceptionThrows);
    }

    [Fact]
    public void Semantic_ConcreteException_NotDetected()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    throw new InvalidOperationException();
                }
            }
            """;
        var result = AnalyzeSemantic(code);
        Assert.Empty(result.SystemExceptionThrows);
    }

    // --- SemanticModel null fallback ---

    [Fact]
    public void NullModel_SyntacticFallback_DetectsBasicPatterns()
    {
        var code = """
            using System;
            class C {
                void Foo() {
                    try { throw new Exception("oops"); }
                    catch (Exception e) { throw new InvalidOperationException("wrap"); }
                }
            }
            """;
        var result = Analyze(code);

        Assert.Single(result.CatchAllClauses);
        Assert.Single(result.MissingInnerExceptions);
        Assert.Single(result.SystemExceptionThrows);
    }
}
