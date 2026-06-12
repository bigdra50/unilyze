namespace Unilyze.Tests;

public sealed class NuGetAssetsReferenceResolverTests : IDisposable
{
    readonly string _tempDir;

    public NuGetAssetsReferenceResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Unilyze_NuGetAssets_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void Resolve_SelectsTfmAndResolvesCompileAssets()
    {
        var packageRoot = Path.Combine(_tempDir, "packages", "sample.package", "1.0.0", "lib", "net8.0");
        Directory.CreateDirectory(packageRoot);
        var dllPath = Path.Combine(packageRoot, "Sample.Package.dll");
        File.WriteAllBytes(dllPath, [0x4D, 0x5A]);

        var csprojDir = Path.Combine(_tempDir, "App");
        Directory.CreateDirectory(Path.Combine(csprojDir, "obj"));
        File.WriteAllText(Path.Combine(csprojDir, "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """);

        var packagesFolder = Path.Combine(_tempDir, "packages") + Path.DirectorySeparatorChar;
        File.WriteAllText(Path.Combine(csprojDir, "obj", "project.assets.json"), $$"""
            {
              "version": 3,
              "targets": {
                "net8.0": {
                  "Sample.Package/1.0.0": {
                    "type": "package",
                    "compile": {
                      "lib/net8.0/Sample.Package.dll": {}
                    }
                  },
                  "Placeholder/1.0.0": {
                    "type": "package",
                    "compile": {
                      "_._": {}
                    }
                  }
                },
                "net10.0": {
                  "Sample.Package/1.0.0": {
                    "type": "package",
                    "compile": {
                      "lib/net8.0/Sample.Package.dll": {}
                    }
                  }
                }
              },
              "libraries": {
                "Sample.Package/1.0.0": {
                  "type": "package",
                  "path": "sample.package/1.0.0"
                },
                "Placeholder/1.0.0": {
                  "type": "package",
                  "path": "missing/placeholder/1.0.0"
                }
              },
              "packageFolders": {
                "{{packagesFolder.Replace("\\", "\\\\")}}": {}
              }
            }
            """);

        var resolved = NuGetAssetsReferenceResolver.Resolve(
            [Path.Combine(csprojDir, "App.csproj")],
            ["net8.0", "net10.0"],
            null,
            "net8.0",
            NullAnalysisLogSink.Null);

        Assert.Single(resolved);
        Assert.Equal(dllPath, resolved[0]);
    }

    [Fact]
    public void Resolve_MissingAssetsJson_ReturnsEmpty()
    {
        var csprojDir = Path.Combine(_tempDir, "NoAssets");
        Directory.CreateDirectory(csprojDir);
        var csproj = Path.Combine(csprojDir, "App.csproj");
        File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var resolved = NuGetAssetsReferenceResolver.Resolve(
            [csproj], null, null, null, NullAnalysisLogSink.Null);

        Assert.Empty(resolved);
    }
}
