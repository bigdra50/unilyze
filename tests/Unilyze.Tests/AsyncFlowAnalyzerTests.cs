using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unilyze.Tests;

public class AsyncFlowAnalyzerTests
{
    static AsyncFlowResult Analyze(string code, string typeName = "C")
    {
        var typeDecl = RoslynTestHelper.GetType(code, typeName);
        return AsyncFlowAnalyzer.Analyze(typeDecl, model: null);
    }

    static AsyncFlowResult AnalyzeSemantic(string code, string typeName = "C")
    {
        var model = RoslynTestHelper.CreateSemanticModel(code);
        var typeDecl = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == typeName);
        return AsyncFlowAnalyzer.Analyze(typeDecl, model);
    }

    // --- AsyncVoidMethod tests ---

    [Fact]
    public void AsyncVoidMethod_Detected_SyntaxOnly()
    {
        var code = """
            class C {
                async void Bad() { }
            }
            """;
        var result = Analyze(code);
        var occurrence = Assert.Single(result.AsyncVoidMethods);
        Assert.Equal("Bad", occurrence.MethodName);
    }

    [Fact]
    public void AsyncVoidMethod_Detected_Semantic()
    {
        var code = """
            class C {
                async void Bad() { }
            }
            """;
        var result = AnalyzeSemantic(code);
        var occurrence = Assert.Single(result.AsyncVoidMethods);
        Assert.Equal("Bad", occurrence.MethodName);
    }

    [Fact]
    public void AsyncVoidLocalFunction_Detected()
    {
        var code = """
            class C {
                void Foo() {
                    async void LocalBad() { }
                }
            }
            """;
        var result = AnalyzeSemantic(code);
        var occurrence = Assert.Single(result.AsyncVoidMethods);
        Assert.Equal("LocalBad", occurrence.MethodName);
    }

    [Fact]
    public void AsyncVoidEventHandler_NotDetected_SyntaxOnly()
    {
        var code = """
            using System;
            class C {
                async void OnClick(object sender, EventArgs e) { }
            }
            """;
        var result = Analyze(code);
        Assert.Empty(result.AsyncVoidMethods);
    }

    [Fact]
    public void AsyncVoidEventHandler_NotDetected_Semantic()
    {
        var code = """
            using System;
            class C {
                async void OnClick(object sender, EventArgs e) { }
            }
            """;
        var result = AnalyzeSemantic(code);
        Assert.Empty(result.AsyncVoidMethods);
    }

    [Fact]
    public void AsyncVoidEventHandler_CustomEventArgs_NotDetected_Semantic()
    {
        var code = """
            using System;
            class MyArgs : EventArgs { }
            class C {
                async void OnClick(object sender, MyArgs e) { }
            }
            """;
        var result = AnalyzeSemantic(code);
        Assert.Empty(result.AsyncVoidMethods);
    }

    [Fact]
    public void AsyncVoidUnityMessage_NotDetected_Semantic()
    {
        var code = """
            namespace UnityEngine { public class MonoBehaviour {} }
            class C : UnityEngine.MonoBehaviour {
                async void Start() { }
            }
            """;
        var result = AnalyzeSemantic(code);
        Assert.Empty(result.AsyncVoidMethods);
    }

    [Fact]
    public void AsyncVoidUnityMessage_NotDetected_SyntaxOnly()
    {
        var code = """
            class C : MonoBehaviour {
                async void Start() { }
            }
            """;
        var result = Analyze(code);
        Assert.Empty(result.AsyncVoidMethods);
    }

    [Fact]
    public void AsyncVoidStart_OnNonMonoBehaviour_Detected()
    {
        var code = """
            class C {
                async void Start() { }
            }
            """;
        var result = AnalyzeSemantic(code);
        var occurrence = Assert.Single(result.AsyncVoidMethods);
        Assert.Equal("Start", occurrence.MethodName);
    }

    [Fact]
    public void AsyncTaskMethod_NotDetected()
    {
        var code = """
            using System.Threading.Tasks;
            class C {
                async Task Good() { await Task.CompletedTask; }
            }
            """;
        var result = AnalyzeSemantic(code);
        Assert.Empty(result.AsyncVoidMethods);
    }

    // --- BlockingTaskWait semantic tests ---

    [Fact]
    public void BlockingWait_Result_Detected_Semantic()
    {
        var code = """
            using System.Threading.Tasks;
            class C {
                void Foo() {
                    var task = Task.CompletedTask;
                    var x = task.Result;
                }
            }
            """;
        var result = AnalyzeSemantic(code);
        var wait = Assert.Single(result.BlockingWaits);
        Assert.Equal("Foo", wait.MethodName);
        Assert.Equal("Result", wait.Pattern);
    }

    [Fact]
    public void BlockingWait_Wait_Detected_Semantic()
    {
        var code = """
            using System.Threading.Tasks;
            class C {
                void Foo() {
                    var task = Task.CompletedTask;
                    task.Wait();
                }
            }
            """;
        var result = AnalyzeSemantic(code);
        var wait = Assert.Single(result.BlockingWaits);
        Assert.Equal("Foo", wait.MethodName);
        Assert.Equal("Wait", wait.Pattern);
    }

    [Fact]
    public void BlockingWait_GetAwaiterGetResult_Detected_Semantic()
    {
        var code = """
            using System.Threading.Tasks;
            class C {
                void Foo() {
                    var task = Task.CompletedTask;
                    task.GetAwaiter().GetResult();
                }
            }
            """;
        var result = AnalyzeSemantic(code);
        var wait = Assert.Single(result.BlockingWaits);
        Assert.Equal("Foo", wait.MethodName);
        Assert.Equal("GetAwaiter().GetResult()", wait.Pattern);
    }

    [Fact]
    public void BlockingWait_TaskOfT_Detected_Semantic()
    {
        var code = """
            using System.Threading.Tasks;
            class C {
                void Foo() {
                    Task<int> task = Task.FromResult(1);
                    var x = task.Result;
                }
            }
            """;
        var result = AnalyzeSemantic(code);
        var wait = Assert.Single(result.BlockingWaits);
        Assert.Equal("Result", wait.Pattern);
    }

    [Fact]
    public void BlockingWait_ValueTaskOfT_Detected_Semantic()
    {
        var code = """
            using System.Threading.Tasks;
            class C {
                void Foo() {
                    ValueTask<int> task = new ValueTask<int>(1);
                    task.Wait();
                }
            }
            """;
        var result = AnalyzeSemantic(code);
        var wait = Assert.Single(result.BlockingWaits);
        Assert.Equal("Wait", wait.Pattern);
    }

    [Fact]
    public void BlockingWait_UniTask_Detected_Semantic()
    {
        var code = """
            namespace Cysharp.Threading.Tasks {
                public struct UniTask {
                    public UniTaskAwaiter GetAwaiter() => default;
                }
                public struct UniTaskAwaiter {
                    public bool IsCompleted => true;
                    public void GetResult() { }
                }
            }
            class C {
                void Foo() {
                    Cysharp.Threading.Tasks.UniTask task = default;
                    task.GetAwaiter().GetResult();
                }
            }
            """;
        var model = RoslynTestHelper.CreateSemanticModel(code);
        var errors = model.Compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(errors);

        var typeDecl = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == "C");
        var result = AsyncFlowAnalyzer.Analyze(typeDecl, model);
        var wait = Assert.Single(result.BlockingWaits);
        Assert.Equal("GetAwaiter().GetResult()", wait.Pattern);
    }

    [Fact]
    public void BlockingWait_SyntaxOnly_GetAwaiterGetResult_Reported()
    {
        var code = """
            class C {
                void Foo() {
                    var task = GetTask();
                    task.GetAwaiter().GetResult();
                }
                object GetTask() => null!;
            }
            """;
        var result = Analyze(code);
        var wait = Assert.Single(result.BlockingWaits);
        Assert.Equal("GetAwaiter().GetResult()", wait.Pattern);
    }

    [Fact]
    public void BlockingWait_SyntaxOnly_Result_NotReported()
    {
        var code = """
            class C {
                void Foo() {
                    var task = GetTask();
                    var x = task.Result;
                }
                object GetTask() => null!;
            }
            """;
        var result = Analyze(code);
        Assert.Empty(result.BlockingWaits);
    }

    [Fact]
    public void BlockingWait_SyntaxOnly_Wait_NotReported()
    {
        var code = """
            class C {
                void Foo() {
                    var task = GetTask();
                    task.Wait();
                }
                object GetTask() => null!;
            }
            """;
        var result = Analyze(code);
        Assert.Empty(result.BlockingWaits);
    }

    [Fact]
    public void BlockingWait_CustomResultProperty_NotDetected_Semantic()
    {
        var code = """
            class Box { public int Result; }
            class C {
                void Foo() {
                    var box = new Box();
                    var x = box.Result;
                }
            }
            """;
        var result = AnalyzeSemantic(code);
        Assert.Empty(result.BlockingWaits);
    }

    // --- Smell detector + SARIF wiring ---

    [Fact]
    public void AsyncFlowSmellDetector_BlockingTaskWait_LineMatchesOccurrence()
    {
        var code = """
            using System.Threading.Tasks;
            class C {
                void Foo() {
                    var task = Task.CompletedTask;
                    task.Wait();
                }
            }
            """;
        var model = RoslynTestHelper.CreateSemanticModel(code);
        var typeDecl = model.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(td => td.Identifier.Text == "C");
        var detected = new AsyncFlowSmellDetector().Detect(typeDecl, model).Single();

        Assert.Equal(CodeSmellKind.BlockingTaskWait, detected.Kind);
        Assert.NotNull(detected.Line);

        var flow = AsyncFlowAnalyzer.Analyze(typeDecl, model);
        Assert.Equal(flow.BlockingWaits[0].Line, detected.Line);
    }

    [Fact]
    public void SarifFormatter_IncludesUni017AndUni018Rules()
    {
        var smells = new List<CodeSmell>
        {
            new(CodeSmellKind.AsyncVoidMethod, SmellSeverity.Warning, "C", "Bad",
                "async void method", Line: 3),
            new(CodeSmellKind.BlockingTaskWait, SmellSeverity.Warning, "C", "Foo",
                "blocking wait", Line: 5),
        };
        var typeMetrics = new TypeMetrics(
            "C", "", "TestAssembly",
            20, 1, 1,
            1.0, 1, 1.0, 1,
            0, 8.0,
            [new MethodMetrics("Foo", 1, 1, 1, 0, 5, StartLine: 2)],
            CodeSmells: smells,
            FilePath: "/project/src/Test.cs",
            StartLine: 1);
        var result = new AnalysisResult(
            "/project", DateTimeOffset.UtcNow, [], [], [], [typeMetrics]);

        var json = SarifFormatter.Generate(result);
        Assert.Contains("\"UNI022\"", json, StringComparison.Ordinal);
        Assert.Contains("\"UNI023\"", json, StringComparison.Ordinal);
    }
}
