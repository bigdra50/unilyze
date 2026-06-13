namespace Unilyze.Tests.Discovery;

public sealed class DotnetRuntimeReferenceResolverTests
{
    [Fact]
    public void Resolve_SyntaxOnlyCap_ShortCircuitsWithoutReferenceDiscovery()
    {
        var resolved = DotnetRuntimeReferenceResolver.Resolve(AnalysisLevel.Syntax);

        Assert.Equal(AnalysisLevel.Syntax, resolved.Level);
        Assert.Empty(resolved.Paths);
    }

    [Fact]
    public void Resolve_ReturnsFrameworkAssembliesOnly()
    {
        var resolved = DotnetRuntimeReferenceResolver.Resolve();

        Assert.NotEqual(AnalysisLevel.Syntax, resolved.Level);
        Assert.NotEmpty(resolved.Paths);
        Assert.All(resolved.Paths, path =>
        {
            Assert.True(File.Exists(path), $"Missing framework assembly: {path}");
            Assert.True(DotnetRuntimeReferenceResolver.IsFrameworkAssembly(path), path);
            Assert.DoesNotContain("Microsoft.CodeAnalysis", path, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains(resolved.Paths, path =>
            Path.GetFileName(path).Equals("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).Equals("System.Runtime.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_RespectsMaxLevelCap()
    {
        var resolved = DotnetRuntimeReferenceResolver.Resolve(AnalysisLevel.Core);

        Assert.Equal(AnalysisLevel.Core, resolved.Level);
        Assert.NotEmpty(resolved.Paths);
    }

    [Theory]
    [InlineData("System.Runtime.dll", true)]
    [InlineData("System.Private.CoreLib.dll", true)]
    [InlineData("mscorlib.dll", true)]
    [InlineData("netstandard.dll", true)]
    [InlineData("Microsoft.CodeAnalysis.dll", false)]
    [InlineData("Unilyze.dll", false)]
    public void IsFrameworkAssembly_FiltersExpectedNames(string fileName, bool expected)
    {
        Assert.Equal(expected, DotnetRuntimeReferenceResolver.IsFrameworkAssembly(fileName));
    }
}
