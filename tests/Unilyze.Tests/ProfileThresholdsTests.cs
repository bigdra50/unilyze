using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Unilyze;
using Unilyze.Tests.Helpers;

namespace Unilyze.Tests;

public sealed class ProfileThresholdsTests
{
    readonly string _tempDir;

    public ProfileThresholdsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-profile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Merge_Profile_ProjectOverridesGlobal()
    {
        var global = new UnilyzeConfig(Profile: "default");
        var project = new UnilyzeConfig(Profile: "unity");
        var merged = UnilyzeConfig.Merge(global, project);
        Assert.Equal("unity", merged.Profile);
    }

    [Fact]
    public void UnityProfile_MonoBehaviourGodClass_NotDetectedWithRelaxedThresholds()
    {
        WriteFile("Target.cs", """
            namespace UnityEngine { public class MonoBehaviour { } }
            public class Player : UnityEngine.MonoBehaviour
            {
                public void M01() { }
                public void M02() { }
                public void M03() { }
                public void M04() { }
                public void M05() { }
                public void M06() { }
                public void M07() { }
                public void M08() { }
                public void M09() { }
                public void M10() { }
                public void M11() { }
                public void M12() { }
                public void M13() { }
                public void M14() { }
                public void M15() { }
                public void M16() { }
                public void M17() { }
                public void M18() { }
                public void M19() { }
                public void M20() { }
                public void M21() { }
            }
            """);

        var defaultResult = AnalyzeWithProfile(SmellThresholdProfiles.DefaultProfileName);
        var unityResult = AnalyzeWithProfile(SmellThresholdProfiles.UnityProfileName);

        var defaultMetrics = defaultResult.TypeMetrics!.Single(m => m.TypeName == "Player");
        var unityMetrics = unityResult.TypeMetrics!.Single(m => m.TypeName == "Player");

        Assert.Contains(defaultMetrics.CodeSmells ?? [], s => s.Kind == CodeSmellKind.GodClass);
        Assert.DoesNotContain(unityMetrics.CodeSmells ?? [], s => s.Kind == CodeSmellKind.GodClass);
        Assert.Equal(TypeRole.MonoBehaviour, defaultResult.Types.Single(t => t.Name == "Player").Role);
    }

    [Fact]
    public void UnityProfile_LowCohesion_IsInformationalNotWarning()
    {
        WriteFile("Target.cs", """
            public class SplitFields
            {
                int _a, _b, _c, _d, _e, _f, _g, _h, _i, _j;
                public void A() { _a = 1; }
                public void B() { _b = 2; }
                public void C() { _c = 3; }
                public void D() { _d = 4; }
                public void E() { _e = 5; }
                public void F() { _f = 6; }
                public void G() { _g = 7; }
                public void H() { _h = 8; }
                public void I() { _i = 9; }
                public void J() { _j = 10; }
            }
            """);

        var defaultResult = AnalyzeWithProfile(SmellThresholdProfiles.DefaultProfileName);
        var unityResult = AnalyzeWithProfile(SmellThresholdProfiles.UnityProfileName);

        var defaultMetrics = defaultResult.TypeMetrics!.Single(m => m.TypeName == "SplitFields");
        var unityMetrics = unityResult.TypeMetrics!.Single(m => m.TypeName == "SplitFields");

        Assert.Contains(defaultMetrics.CodeSmells ?? [], s => s.Kind == CodeSmellKind.LowCohesion);
        Assert.DoesNotContain(unityMetrics.CodeSmells ?? [], s => s.Kind == CodeSmellKind.LowCohesion);
        Assert.Equal(1, unityMetrics.InformationalCount);
    }

    [Fact]
    public void UserSmellOverride_TakesPrecedenceOverProfile()
    {
        WriteFile(".unilyze.json", """
            {
                "profile": "unity",
                "smells": {
                    "GodClass": { "methods": 10 }
                }
            }
            """);
        WriteFile("Target.cs", """
            namespace UnityEngine { public class MonoBehaviour { } }
            public class Player : UnityEngine.MonoBehaviour
            {
                public void M01() { }
                public void M02() { }
                public void M03() { }
                public void M04() { }
                public void M05() { }
                public void M06() { }
                public void M07() { }
                public void M08() { }
                public void M09() { }
                public void M10() { }
                public void M11() { }
            }
            """);

        var config = UnilyzeConfig.LoadMerged(_tempDir);
        var resolved = config.ResolveAnalysisConfig();
        var result = AnalysisPipeline.Build(_tempDir, null, null, config.ExcludeDirs, analysisConfig: resolved);
        var metrics = result.TypeMetrics!.Single(m => m.TypeName == "Player");

        Assert.Contains(metrics.CodeSmells ?? [], s => s.Kind == CodeSmellKind.GodClass);
    }

    AnalysisResult AnalyzeWithProfile(string profile)
    {
        WriteFile(".unilyze.json", $$"""{"profile":"{{profile}}","disableDefaultExcludes":true}""");
        var config = UnilyzeConfig.LoadMerged(_tempDir);
        return AnalysisPipeline.Build(_tempDir, null, null, config.ExcludeDirs,
            excludeGeneratedCode: false,
            applyAnyDepthExcludes: false,
            analysisConfig: config.ResolveAnalysisConfig());
    }

    void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}

public sealed class ProfileGoldenE2eTests
{
    static readonly string FixtureRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "golden"));

    static readonly string CurrentTargetFramework = GoldenCorpusTestsHelper.CurrentTargetFramework;
    static readonly string DotnetHostPath = GoldenCorpusTestsHelper.DotnetHostPath;
    static readonly string AppDllPath = GoldenCorpusTestsHelper.AppDllPath;

    [Fact]
    public void GoldenFixture_UnityProfile_ChangesMonoBehaviourGodClassAndLowCohesion()
    {
        GoldenCorpusTestsHelper.EnsureCsprojWithCoreEngineReference(FixtureRoot);

        var defaultJsonPath = Path.Combine(Path.GetTempPath(), $"unilyze-default-{Guid.NewGuid():N}.json");
        var unityJsonPath = Path.Combine(Path.GetTempPath(), $"unilyze-unity-{Guid.NewGuid():N}.json");

        try
        {
            var (defaultExit, _, _) = Run("-p", FixtureRoot, "-f", "json", "-o", defaultJsonPath);
            var (unityExit, _, diffErr) = Run("-p", FixtureRoot, "-f", "json", "-o", unityJsonPath, "--profile", "unity");
            Assert.Equal(0, defaultExit);
            Assert.Equal(0, unityExit);

            var defaultOut = File.ReadAllText(defaultJsonPath);
            var unityOut = File.ReadAllText(unityJsonPath);
            var defaultRoot = JsonNode.Parse(defaultOut)!.AsObject();
            var unityRoot = JsonNode.Parse(unityOut)!.AsObject();
            Assert.Null(defaultRoot["profile"]);
            Assert.Equal("unity", unityRoot["profile"]?.GetValue<string>());

            var defaultMbGod = FindTypeMetrics(defaultRoot, "MonoBehaviourGodClassTarget");
            var unityMbGod = FindTypeMetrics(unityRoot, "MonoBehaviourGodClassTarget");
            Assert.Contains(defaultMbGod["codeSmells"]!.AsArray(), s => s!["kind"]!.GetValue<string>() == "GodClass");
            Assert.DoesNotContain(unityMbGod["codeSmells"]?.AsArray() ?? [], s => s?["kind"]?.GetValue<string>() == "GodClass");
            Assert.Equal("MonoBehaviour", FindType(defaultRoot, "MonoBehaviourGodClassTarget")["role"]!.GetValue<string>());

            var defaultMbLcom = FindTypeMetrics(defaultRoot, "MonoBehaviourLowCohesionTarget");
            var unityMbLcom = FindTypeMetrics(unityRoot, "MonoBehaviourLowCohesionTarget");
            Assert.Contains(defaultMbLcom["codeSmells"]!.AsArray(), s => s!["kind"]!.GetValue<string>() == "LowCohesion");
            Assert.DoesNotContain(unityMbLcom["codeSmells"]?.AsArray() ?? [], s => s?["kind"]?.GetValue<string>() == "LowCohesion");
            Assert.Equal(1, unityMbLcom["informationalCount"]!.GetValue<int>());

            var (_, _, diffStderr) = Run("diff", defaultJsonPath, unityJsonPath);
            Assert.Contains("profiles differ", diffStderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(defaultJsonPath)) File.Delete(defaultJsonPath);
            if (File.Exists(unityJsonPath)) File.Delete(unityJsonPath);
        }
    }

    static JsonObject FindTypeMetrics(JsonObject root, string typeName)
        => root["typeMetrics"]!.AsArray()
            .Select(n => n!.AsObject())
            .Single(o => o["typeName"]!.GetValue<string>() == typeName);

    static JsonObject FindType(JsonObject root, string typeName)
        => root["types"]!.AsArray()
            .Select(n => n!.AsObject())
            .Single(o => o["name"]!.GetValue<string>() == typeName);

    static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = DotnetHostPath,
        };
        psi.ArgumentList.Add(AppDllPath);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        return TestProcessRunner.Run(psi, 120_000);
    }
}

internal static class GoldenCorpusTestsHelper
{
    internal static string CurrentTargetFramework { get; } = ResolveCurrentTargetFramework();
    internal static string DotnetHostPath { get; } = ResolveDotnetHostPath();
    internal static string AppDllPath { get; } = ResolveAppDllPath();

    internal static void EnsureCsprojWithCoreEngineReference(string fixtureRoot)
    {
        var csprojPath = Path.Combine(fixtureRoot, "Golden.csproj");
        var dllPath = typeof(object).Assembly.Location;
        File.WriteAllText(csprojPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Reference Include="CoreLib">
                  <HintPath>{dllPath}</HintPath>
                </Reference>
              </ItemGroup>
            </Project>
            """);
    }

    static string ResolveCurrentTargetFramework()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var tfm = Path.GetFileName(baseDir);
        if (string.IsNullOrWhiteSpace(tfm) || !tfm.StartsWith("net", StringComparison.Ordinal))
            throw new InvalidOperationException($"Could not infer target framework from base directory: {AppContext.BaseDirectory}");
        return tfm;
    }

    static string ResolveDotnetHostPath()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return string.IsNullOrWhiteSpace(configured) ? "dotnet" : configured;
    }

    static string ResolveAppDllPath()
    {
        var path = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Unilyze", "bin", "Debug", CurrentTargetFramework, "Unilyze.dll"));

        if (!File.Exists(path))
            throw new FileNotFoundException($"Could not find CLI assembly under test: {path}", path);

        return path;
    }
}
