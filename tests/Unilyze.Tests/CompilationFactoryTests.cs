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
        var resolved = new ResolvedDlls(AnalysisLevel.Core, []);
        var result = CompilationFactory.Create(resolved, SingleTree);

        Assert.Null(result.Compilation);
        Assert.Equal(AnalysisLevel.Syntax, result.Level);
    }

    [Fact]
    public void SyntaxOnly_WhenLevelIsSyntaxOnly()
    {
        var resolved = new ResolvedDlls(AnalysisLevel.Syntax, [ValidDllPath]);
        var result = CompilationFactory.Create(resolved, SingleTree);

        Assert.Null(result.Compilation);
        Assert.Equal(AnalysisLevel.Syntax, result.Level);
    }

    [Fact]
    public void CreatesCompilation_WithValidReference()
    {
        var resolved = new ResolvedDlls(AnalysisLevel.Core, [ValidDllPath]);
        var result = CompilationFactory.Create(resolved, SingleTree);

        Assert.NotNull(result.Compilation);
        Assert.Equal(AnalysisLevel.Core, result.Level);
    }

    [Fact]
    public void MergesCsprojReferencePaths()
    {
        // Start with SyntaxOnly + empty paths; CsprojInfo provides a valid reference.
        // The merge logic upgrades SyntaxOnly -> CoreEngine when merged list is non-empty.
        var resolved = new ResolvedDlls(AnalysisLevel.Syntax, []);
        var csprojInfo = new CsprojInfo([ValidDllPath], [], [], null, []);

        var result = CompilationFactory.Create(resolved, SingleTree, csprojInfo);

        Assert.NotNull(result.Compilation);
        Assert.Equal(AnalysisLevel.Core, result.Level);
    }

    [Fact]
    public void SyntaxOnlyCap_SkipsCsprojElevation()
    {
        // A SyntaxOnly pin must win over the csproj merge: without the cap this
        // exact input re-elevates to CoreEngine (see MergesCsprojReferencePaths).
        var resolved = new ResolvedDlls(AnalysisLevel.Syntax, []);
        var csprojInfo = new CsprojInfo([ValidDllPath], [], [], null, []);

        var result = CompilationFactory.Create(
            resolved, SingleTree, csprojInfo, maxLevel: AnalysisLevel.Syntax);

        Assert.Null(result.Compilation);
        Assert.Equal(AnalysisLevel.Syntax, result.Level);
    }

    [Fact]
    public void SyntaxOnly_WhenAllReferencesFail()
    {
        var resolved = new ResolvedDlls(AnalysisLevel.Core,
            ["/nonexistent/fake1.dll", "/nonexistent/fake2.dll"]);

        var result = CompilationFactory.Create(resolved, SingleTree);

        Assert.Null(result.Compilation);
        Assert.Equal(AnalysisLevel.Syntax, result.Level);
    }

    [Fact]
    public void DowngradesToSyntaxOnly_WhenMajorityFail()
    {
        // 2 out of 3 paths are invalid -> failRatio = 0.666 > 0.5 -> downgrade
        var resolved = new ResolvedDlls(AnalysisLevel.Full,
            [ValidDllPath, "/nonexistent/fake1.dll", "/nonexistent/fake2.dll"]);

        var result = CompilationFactory.Create(resolved, SingleTree);

        Assert.Null(result.Compilation);
        Assert.Equal(AnalysisLevel.Syntax, result.Level);
    }

    [Fact]
    public void ContinuesWithPartialReferences_WhenMinorityFail()
    {
        // 1 out of 3 paths is invalid -> failRatio = 0.333 <= 0.5 -> continue
        var resolved = new ResolvedDlls(AnalysisLevel.Full,
            [ValidDllPath, ValidDllPath, "/nonexistent/fake.dll"]);

        var result = CompilationFactory.Create(resolved, SingleTree);

        Assert.NotNull(result.Compilation);
        Assert.Equal(AnalysisLevel.Full, result.Level);
    }
}
