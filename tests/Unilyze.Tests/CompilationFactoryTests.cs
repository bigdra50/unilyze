using Microsoft.CodeAnalysis.CSharp;

namespace Unilyze.Tests;

public class CompilationFactoryTests
{
    static readonly string ValidDllPath = typeof(object).Assembly.Location;

    static IReadOnlyList<Microsoft.CodeAnalysis.SyntaxTree> EmptyTrees => [];

    static IReadOnlyList<Microsoft.CodeAnalysis.SyntaxTree> SingleTree =>
        [CSharpSyntaxTree.ParseText("class C { }")];

    [Fact]
    public void SyntaxOnly_WhenNoPaths()
    {
        var resolved = new ResolvedDlls(AnalysisLevel.CoreEngine, []);
        var result = CompilationFactory.Create(resolved, SingleTree);

        Assert.Null(result.Compilation);
        Assert.Equal(AnalysisLevel.SyntaxOnly, result.Level);
    }

    [Fact]
    public void SyntaxOnly_WhenLevelIsSyntaxOnly()
    {
        var resolved = new ResolvedDlls(AnalysisLevel.SyntaxOnly, [ValidDllPath]);
        var result = CompilationFactory.Create(resolved, SingleTree);

        Assert.Null(result.Compilation);
        Assert.Equal(AnalysisLevel.SyntaxOnly, result.Level);
    }

    [Fact]
    public void CreatesCompilation_WithValidReference()
    {
        var resolved = new ResolvedDlls(AnalysisLevel.CoreEngine, [ValidDllPath]);
        var result = CompilationFactory.Create(resolved, SingleTree);

        Assert.NotNull(result.Compilation);
        Assert.Equal(AnalysisLevel.CoreEngine, result.Level);
    }

    [Fact]
    public void MergesCsprojReferencePaths()
    {
        // Start with SyntaxOnly + empty paths; CsprojInfo provides a valid reference.
        // The merge logic upgrades SyntaxOnly -> CoreEngine when merged list is non-empty.
        var resolved = new ResolvedDlls(AnalysisLevel.SyntaxOnly, []);
        var csprojInfo = new CsprojInfo([ValidDllPath], [], [], null);

        var result = CompilationFactory.Create(resolved, SingleTree, csprojInfo);

        Assert.NotNull(result.Compilation);
        Assert.Equal(AnalysisLevel.CoreEngine, result.Level);
    }

    [Fact]
    public void SyntaxOnly_WhenAllReferencesFail()
    {
        var resolved = new ResolvedDlls(AnalysisLevel.CoreEngine,
            ["/nonexistent/fake1.dll", "/nonexistent/fake2.dll"]);

        var result = CompilationFactory.Create(resolved, SingleTree);

        Assert.Null(result.Compilation);
        Assert.Equal(AnalysisLevel.SyntaxOnly, result.Level);
    }

    [Fact]
    public void DowngradesToSyntaxOnly_WhenMajorityFail()
    {
        // 2 out of 3 paths are invalid -> failRatio = 0.666 > 0.5 -> downgrade
        var resolved = new ResolvedDlls(AnalysisLevel.FullEngine,
            [ValidDllPath, "/nonexistent/fake1.dll", "/nonexistent/fake2.dll"]);

        var result = CompilationFactory.Create(resolved, SingleTree);

        Assert.Null(result.Compilation);
        Assert.Equal(AnalysisLevel.SyntaxOnly, result.Level);
    }

    [Fact]
    public void ContinuesWithPartialReferences_WhenMinorityFail()
    {
        // 1 out of 3 paths is invalid -> failRatio = 0.333 <= 0.5 -> continue
        var resolved = new ResolvedDlls(AnalysisLevel.FullEngine,
            [ValidDllPath, ValidDllPath, "/nonexistent/fake.dll"]);

        var result = CompilationFactory.Create(resolved, SingleTree);

        Assert.NotNull(result.Compilation);
        Assert.Equal(AnalysisLevel.FullEngine, result.Level);
    }
}
