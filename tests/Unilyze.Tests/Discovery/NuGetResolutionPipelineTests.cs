namespace Unilyze.Tests.Discovery;

public sealed class NuGetResolutionPipelineTests : IDisposable
{
    readonly string _tempDir;

    public NuGetResolutionPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Unilyze_NuGetPipeline_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void Build_DefaultAndResolveNuget_ProduceIdenticalTypeCounts()
    {
        var defaultResult = AnalysisPipeline.Build(_tempDir, null, null);
        var nugetResult = AnalysisPipeline.Build(_tempDir, null, null, resolveNuget: true);
        Assert.Equal(defaultResult.Types.Count, nugetResult.Types.Count);
        Assert.Null(defaultResult.ResolveNuget);
        Assert.True(nugetResult.ResolveNuget);
    }

    [Fact]
    public void Build_WithPackageAssets_ResolvesNuGetDllsWhenEnabled()
    {
        var packageRoot = Path.Combine(_tempDir, "packages", "sample.package", "1.0.0", "lib", "net8.0");
        Directory.CreateDirectory(packageRoot);
        var dllPath = Path.Combine(packageRoot, "Sample.Package.dll");
        File.WriteAllBytes(dllPath, [0x4D, 0x5A]);

        Directory.CreateDirectory(Path.Combine(_tempDir, "obj"));
        File.WriteAllText(Path.Combine(_tempDir, "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_tempDir, "Types.cs"), """
            namespace Sample;
            public class AppType { }
            """);

        var packagesFolder = Path.Combine(_tempDir, "packages") + Path.DirectorySeparatorChar;
        File.WriteAllText(Path.Combine(_tempDir, "obj", "project.assets.json"), $$"""
            {
              "version": 3,
              "targets": {
                "net8.0": {
                  "Sample.Package/1.0.0": {
                    "type": "package",
                    "compile": { "lib/net8.0/Sample.Package.dll": {} }
                  }
                }
              },
              "libraries": {
                "Sample.Package/1.0.0": { "type": "package", "path": "sample.package/1.0.0" }
              },
              "packageFolders": { "{{packagesFolder.Replace("\\", "\\\\")}}": {} }
            }
            """);

        var csproj = Path.Combine(_tempDir, "App.csproj");
        var dlls = NuGetAssetsReferenceResolver.Resolve(
            [csproj], ["net8.0"], null, "net8.0", NullAnalysisLogSink.Null);
        Assert.Single(dlls);

        var with = AnalysisPipeline.Build(_tempDir, null, null, resolveNuget: true);
        Assert.True(with.ResolveNuget);
        Assert.Equal("net8.0", with.TargetFramework);
        Assert.Equal(AnalysisPipeline.Build(_tempDir, null, null).Types.Count, with.Types.Count);
    }

    [Fact]
    public void Build_IncludeGenerated_DoesNotChangeTypeCounts()
    {
        var csprojDir = _tempDir;
        Directory.CreateDirectory(Path.Combine(csprojDir, "obj", "Debug", "net8.0", "generated"));
        File.WriteAllText(Path.Combine(csprojDir, "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(csprojDir, "UserPartial.cs"), """
            namespace Sample;

            public partial class JsonContext
            {
                public static int UserValue => 1;
            }
            """);
        File.WriteAllText(Path.Combine(csprojDir, "obj", "Debug", "net8.0", "generated", "JsonContext.g.cs"), """
            namespace Sample;

            public partial class JsonContext
            {
                public static int GeneratedValue => 2;
            }
            """);

        var baseline = AnalysisPipeline.Build(_tempDir, null, null);
        var withGenerated = AnalysisPipeline.Build(_tempDir, null, null, includeGenerated: true);

        Assert.Equal(baseline.Types.Count, withGenerated.Types.Count);
        Assert.True(withGenerated.IncludeGenerated);
    }
}
