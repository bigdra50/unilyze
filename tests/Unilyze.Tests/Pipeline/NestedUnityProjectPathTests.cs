namespace Unilyze.Tests.Pipeline;

// How -p is reflected in the recorded project path and the file paths of a repository
// that keeps its Unity project in a subfolder. Runs with the working directory set to
// the repository root, because only a relative -p shows the difference.
[Collection(WorkingDirectoryCollection.Name)]
public sealed class NestedUnityProjectPathTests : IDisposable
{
    readonly string _tempDir;
    readonly IDisposable _cwdScope;

    public NestedUnityProjectPathTests(WorkingDirectoryGate cwdGate)
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Unilyze_NestedUnityPath_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        WriteFile("unity/ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.0.0f1");
        WriteFile("unity/Assets/Scripts/Player.cs", "namespace Game; public class Player { }");
        _cwdScope = cwdGate.Enter(_tempDir);
    }

    public void Dispose()
    {
        _cwdScope.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [Theory]
    [InlineData("unity")]
    [InlineData("unity/")]
    public void Build_RelativePathToTheProjectItself_KeepsFilePathsRelativeAsGiven(string path)
    {
        var result = AnalysisPipeline.Build(path, null, null);

        Assert.Equal("unity", result.ProjectKind);
        Assert.Equal(Path.GetFullPath(path), result.ProjectPath);
        var metrics = Assert.Single(result.TypeMetrics!);
        // The scan keeps the prefix as given, so "unity/" yields "unity/Assets\..." on
        // Windows; the shape is what matters here, not the separator.
        Assert.Equal("unity/Assets/Scripts/Player.cs", metrics.FilePath.Replace('\\', '/'));
    }

    [Fact]
    public void Build_CurrentDirectoryAboveOneUnityProject_ResolvesIntoIt()
    {
        var result = AnalysisPipeline.Build(".", null, null);

        Assert.Equal("unity", result.ProjectKind);
        Assert.Equal(Path.GetFullPath("unity"), result.ProjectPath);
        var metrics = Assert.Single(result.TypeMetrics!);
        Assert.Equal(Path.GetFullPath(Path.Combine("unity", "Assets", "Scripts", "Player.cs")), metrics.FilePath);
    }
}
