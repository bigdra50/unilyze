using Unilyze;

namespace Unilyze.Tests;

public sealed class ProgramHelpersTests
{
    // --- ParseOptions ---

    [Fact]
    public void ParseOptions_EmptyArgs_ReturnsEmpty()
    {
        var result = ProgramHelpers.ParseOptions([]);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("-v")]
    [InlineData("--version")]
    public void ParseOptions_BooleanFlags_SetToTrue(string flag)
    {
        var result = ProgramHelpers.ParseOptions([flag]);
        Assert.Equal("true", result[flag]);
    }

    [Fact]
    public void ParseOptions_KeyValuePair_ParsedCorrectly()
    {
        var result = ProgramHelpers.ParseOptions(["-p", "/some/path"]);
        Assert.Equal("/some/path", result["-p"]);
    }

    [Fact]
    public void ParseOptions_MixedFlagsAndKeyValues()
    {
        var result = ProgramHelpers.ParseOptions(["-p", "/path", "-f", "json", "--help"]);
        Assert.Equal("/path", result["-p"]);
        Assert.Equal("json", result["-f"]);
        Assert.Equal("true", result["--help"]);
    }

    [Fact]
    public void ParseOptions_PositionalArgsIgnored()
    {
        var result = ProgramHelpers.ParseOptions(["somefile.json", "-o", "out.json"]);
        Assert.Single(result);
        Assert.Equal("out.json", result["-o"]);
    }

    [Fact]
    public void ParseOptions_TrailingKeyWithoutValue_Ignored()
    {
        var result = ProgramHelpers.ParseOptions(["-p"]);
        Assert.Empty(result);
    }

    // --- ResolveFormat ---

    [Theory]
    [InlineData("json", OutputFormat.Json)]
    [InlineData("html", OutputFormat.Html)]
    [InlineData("sarif", OutputFormat.Sarif)]
    [InlineData("JSON", OutputFormat.Json)]
    [InlineData("Html", OutputFormat.Html)]
    public void ResolveFormat_ExplicitFormat_ReturnsCorrect(string fmt, OutputFormat expected)
    {
        Assert.Equal(expected, ProgramHelpers.ResolveFormat(fmt, null));
    }

    [Fact]
    public void ResolveFormat_InvalidFormat_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => ProgramHelpers.ResolveFormat("csv", null));
        Assert.Contains("csv", ex.Message);
    }

    [Theory]
    [InlineData("out.json", OutputFormat.Json)]
    [InlineData("out.html", OutputFormat.Html)]
    [InlineData("out.htm", OutputFormat.Html)]
    [InlineData("out.sarif", OutputFormat.Sarif)]
    public void ResolveFormat_InferFromExtension(string output, OutputFormat expected)
    {
        Assert.Equal(expected, ProgramHelpers.ResolveFormat(null, output));
    }

    [Fact]
    public void ResolveFormat_UnknownExtension_DefaultsToJson()
    {
        Assert.Equal(OutputFormat.Json, ProgramHelpers.ResolveFormat(null, "out.txt"));
    }

    [Fact]
    public void ResolveFormat_NoBothArgs_DefaultsToHtml()
    {
        Assert.Equal(OutputFormat.Html, ProgramHelpers.ResolveFormat(null, null));
    }

    [Fact]
    public void ResolveFormat_ExplicitOverridesExtension()
    {
        Assert.Equal(OutputFormat.Json, ProgramHelpers.ResolveFormat("json", "out.html"));
    }

    // --- FilterAssemblies ---

    [Fact]
    public void FilterAssemblies_NoFilter_ReturnsAll()
    {
        var asmdefs = new List<AsmdefInfo>
        {
            new("App.Core", "/dir1", []),
            new("App.UI", "/dir2", [])
        };
        var result = ProgramHelpers.FilterAssemblies(asmdefs, null, null);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterAssemblies_ExactNameMatch()
    {
        var asmdefs = new List<AsmdefInfo>
        {
            new("App.Core", "/dir1", []),
            new("App.UI", "/dir2", [])
        };
        var result = ProgramHelpers.FilterAssemblies(asmdefs, null, "App.Core");
        Assert.Single(result);
        Assert.Equal("App.Core", result[0].Name);
    }

    [Fact]
    public void FilterAssemblies_SuffixMatch()
    {
        var asmdefs = new List<AsmdefInfo>
        {
            new("App.Core", "/dir1", []),
            new("App.UI", "/dir2", [])
        };
        var result = ProgramHelpers.FilterAssemblies(asmdefs, null, "Core");
        Assert.Single(result);
        Assert.Equal("App.Core", result[0].Name);
    }

    [Fact]
    public void FilterAssemblies_PrefixFilter()
    {
        var asmdefs = new List<AsmdefInfo>
        {
            new("App.Core", "/dir1", []),
            new("App.UI", "/dir2", []),
            new("Lib.Util", "/dir3", [])
        };
        var result = ProgramHelpers.FilterAssemblies(asmdefs, "App.", null);
        Assert.Equal(2, result.Count);
        Assert.All(result, a => Assert.StartsWith("App.", a.Name));
    }

    [Fact]
    public void FilterAssemblies_NotFound_ThrowsInvalidOperationException()
    {
        var asmdefs = new List<AsmdefInfo> { new("App.Core", "/dir1", []) };
        Assert.Throws<InvalidOperationException>(() =>
            ProgramHelpers.FilterAssemblies(asmdefs, null, "NonExistent"));
    }

    // --- DetectCommonPrefix ---

    [Fact]
    public void DetectCommonPrefix_SingleAssembly_ReturnsNull()
    {
        var asmdefs = new List<AsmdefInfo> { new("App.Core", "/dir", []) };
        Assert.Null(ProgramHelpers.DetectCommonPrefix(asmdefs));
    }

    [Fact]
    public void DetectCommonPrefix_CommonPrefix_Detected()
    {
        var asmdefs = new List<AsmdefInfo>
        {
            new("App.Core", "/dir1", []),
            new("App.UI", "/dir2", []),
            new("App.Domain", "/dir3", [])
        };
        Assert.Equal("App.", ProgramHelpers.DetectCommonPrefix(asmdefs));
    }

    [Fact]
    public void DetectCommonPrefix_NoCommonPrefix_ReturnsNull()
    {
        var asmdefs = new List<AsmdefInfo>
        {
            new("Alpha.Core", "/dir1", []),
            new("Beta.UI", "/dir2", [])
        };
        Assert.Null(ProgramHelpers.DetectCommonPrefix(asmdefs));
    }

    [Fact]
    public void DetectCommonPrefix_MultiLevelPrefix()
    {
        var asmdefs = new List<AsmdefInfo>
        {
            new("Company.App.Core", "/dir1", []),
            new("Company.App.UI", "/dir2", [])
        };
        Assert.Equal("Company.App.", ProgramHelpers.DetectCommonPrefix(asmdefs));
    }

    // --- ResolveAssetsDir ---

    [Fact]
    public void ResolveAssetsDir_HasAssetsSubdir_ReturnsAssetsPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "Assets"));
            var result = ProgramHelpers.ResolveAssetsDir(tempDir);
            Assert.Equal(Path.Combine(tempDir, "Assets"), result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ResolveAssetsDir_IsAssetsDir_ReturnsSame()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-test-{Guid.NewGuid():N}", "Assets");
        try
        {
            Directory.CreateDirectory(tempDir);
            var result = ProgramHelpers.ResolveAssetsDir(tempDir);
            Assert.Equal(tempDir, result);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(tempDir)!, true);
        }
    }

    [Fact]
    public void ResolveAssetsDir_NoAssets_ReturnsSame()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var result = ProgramHelpers.ResolveAssetsDir(tempDir);
            Assert.Equal(tempDir, result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
