namespace Unilyze.Tests;

public sealed class UnityDllResolverTests : IDisposable
{
    readonly string _tempDir;

    public UnityDllResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Unilyze_UnityDllResolverTests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void Resolve_NonUnityProject_ReturnsSyntaxOnly()
    {
        // No ProjectVersion.txt -> version cannot be detected -> SyntaxOnly regardless of cap.
        var resolved = UnityDllResolver.Resolve(_tempDir);

        Assert.Equal(AnalysisLevel.Syntax, resolved.Level);
        Assert.Empty(resolved.Paths);
    }

    [Fact]
    public void Resolve_SyntaxOnlyCap_ShortCircuitsWithoutDllDiscovery()
    {
        // Even if a Unity version were present, a SyntaxOnly cap must yield SyntaxOnly with no paths.
        Directory.CreateDirectory(Path.Combine(_tempDir, "ProjectSettings"));
        File.WriteAllText(
            Path.Combine(_tempDir, "ProjectSettings", "ProjectVersion.txt"),
            "m_EditorVersion: 2022.3.45f1\n");

        var resolved = UnityDllResolver.Resolve(_tempDir, AnalysisLevel.Syntax);

        Assert.Equal(AnalysisLevel.Syntax, resolved.Level);
        Assert.Empty(resolved.Paths);
    }
}
