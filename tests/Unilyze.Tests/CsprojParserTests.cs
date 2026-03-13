namespace Unilyze.Tests;

public class CsprojParserTests : IDisposable
{
    readonly List<string> _tempFiles = [];
    readonly List<string> _tempDirs = [];

    string CreateTempFile(string content, string extension = ".csproj")
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        var path = Path.Combine(dir, "Test" + extension);
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    // --- TryParse ---

    [Fact]
    public void TryParse_MissingFile_ReturnsNull()
    {
        var result = CsprojParser.TryParse("/nonexistent/path/missing.csproj");

        Assert.Null(result);
    }

    [Fact]
    public void TryParse_ValidCsproj_ReturnsInfo()
    {
        var path = CreateTempFile("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = CsprojParser.TryParse(path);

        Assert.NotNull(result);
        Assert.Empty(result.ReferencePaths);
        Assert.Empty(result.ProjectReferences);
        Assert.Empty(result.DefineConstants);
        Assert.Null(result.LangVersion);
    }

    [Fact]
    public void TryParse_MalformedXml_ReturnsNull()
    {
        var path = CreateTempFile("<Project><Broken>");

        var result = CsprojParser.TryParse(path);

        Assert.Null(result);
    }

    [Fact]
    public void TryParse_ExtractsDefineConstants()
    {
        var path = CreateTempFile("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <DefineConstants>UNITY_EDITOR;UNITY_2021</DefineConstants>
              </PropertyGroup>
            </Project>
            """);

        var result = CsprojParser.TryParse(path);

        Assert.NotNull(result);
        Assert.Equal(2, result.DefineConstants.Count);
        Assert.Contains("UNITY_EDITOR", result.DefineConstants);
        Assert.Contains("UNITY_2021", result.DefineConstants);
    }

    [Fact]
    public void TryParse_ExtractsLangVersion()
    {
        var path = CreateTempFile("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <LangVersion>12.0</LangVersion>
              </PropertyGroup>
            </Project>
            """);

        var result = CsprojParser.TryParse(path);

        Assert.NotNull(result);
        Assert.Equal("12.0", result.LangVersion);
    }

    [Fact]
    public void TryParse_ExtractsProjectReferences()
    {
        var path = CreateTempFile("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Lib\Lib.csproj" />
                <ProjectReference Include="..\Core\Core.csproj" />
              </ItemGroup>
            </Project>
            """);

        var result = CsprojParser.TryParse(path);

        Assert.NotNull(result);
        Assert.Equal(2, result.ProjectReferences.Count);
        Assert.Contains(@"..\Lib\Lib.csproj", result.ProjectReferences);
        Assert.Contains(@"..\Core\Core.csproj", result.ProjectReferences);
    }

    [Fact]
    public void TryParse_MultipleDefineConstants_Deduplicated()
    {
        var path = CreateTempFile("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <DefineConstants>DEBUG;TRACE;DEBUG</DefineConstants>
              </PropertyGroup>
              <PropertyGroup Condition="'$(Configuration)'=='Debug'">
                <DefineConstants>DEBUG;EXTRA</DefineConstants>
              </PropertyGroup>
            </Project>
            """);

        var result = CsprojParser.TryParse(path);

        Assert.NotNull(result);
        // DEBUG appears in both PropertyGroups but should be deduplicated
        Assert.Equal(1, result.DefineConstants.Count(d => d == "DEBUG"));
        Assert.Contains("TRACE", result.DefineConstants);
        Assert.Contains("EXTRA", result.DefineConstants);
    }

    // --- DiscoverCsprojFiles ---

    [Fact]
    public void DiscoverCsprojFiles_EmptyDirectory_ReturnsEmpty()
    {
        var dir = CreateTempDir();

        var result = CsprojParser.DiscoverCsprojFiles(dir);

        Assert.Empty(result);
    }

    [Fact]
    public void DiscoverCsprojFiles_FindsCsprojDirectly()
    {
        var dir = CreateTempDir();
        var csprojPath = Path.Combine(dir, "App.csproj");
        File.WriteAllText(csprojPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var result = CsprojParser.DiscoverCsprojFiles(dir);

        Assert.Single(result);
        Assert.Equal(Path.GetFullPath(csprojPath), result[0]);
    }

    [Fact]
    public void DiscoverCsprojFiles_ExcludesLibraryDir()
    {
        var dir = CreateTempDir();

        // Create a .csproj in a Library/ subdirectory (should be excluded)
        var libraryDir = Path.Combine(dir, "Library");
        Directory.CreateDirectory(libraryDir);
        File.WriteAllText(Path.Combine(libraryDir, "Cached.csproj"), "<Project />");

        // Create a .csproj outside Library/ (should be included)
        var srcDir = Path.Combine(dir, "src");
        Directory.CreateDirectory(srcDir);
        var validPath = Path.Combine(srcDir, "App.csproj");
        File.WriteAllText(validPath, "<Project />");

        var result = CsprojParser.DiscoverCsprojFiles(dir);

        Assert.Single(result);
        Assert.DoesNotContain(result, p => p.Contains("Library"));
    }
}
