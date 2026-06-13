using System.Diagnostics;
using System.Text.Json.Nodes;
using Unilyze.Tests.Helpers;

namespace Unilyze.Tests;

public sealed class EcsE2eTests : IDisposable
{
    readonly string _tempDir;
    static readonly string CurrentTargetFramework = ResolveCurrentTargetFramework();
    static readonly string DotnetHostPath = ResolveDotnetHostPath();
    static readonly string AppDllPath = ResolveAppDllPath();

    public EcsE2eTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-ecs-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        WriteEcsProject();
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDir))
            return;

        foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void E2E_DetectsUni024AndUni025_OnOffenders()
    {
        var (exitCode, stdout, _) = Run("-p", _tempDir, "-f", "sarif");
        Assert.Equal(0, exitCode);

        var doc = JsonNode.Parse(stdout)!;
        var ruleIds = doc["runs"]![0]!["results"]!
            .AsArray()
            .Select(r => r!["ruleId"]!.GetValue<string>())
            .ToList();

        Assert.Contains("UNI024", ruleIds);
        Assert.Contains("UNI025", ruleIds);
        Assert.DoesNotContain(ruleIds, id => id is "UNI026");
    }

    [Fact]
    public void E2E_AnnotatedAndManagedClass_NoUni024Uni025()
    {
        var cleanDir = Path.Combine(_tempDir, "clean");
        Directory.CreateDirectory(cleanDir);
        WriteCleanEcsProject(cleanDir);

        var (exitCode, stdout, _) = Run("-p", cleanDir, "-f", "sarif");
        Assert.Equal(0, exitCode);

        var doc = JsonNode.Parse(stdout)!;
        var ruleIds = doc["runs"]![0]!["results"]!
            .AsArray()
            .Select(r => r!["ruleId"]!.GetValue<string>())
            .Where(id => id is "UNI024" or "UNI025")
            .ToList();

        Assert.Empty(ruleIds);
    }

    [Fact]
    public void E2E_BurstCoverageAppearsInJson()
    {
        var (exitCode, stdout, _) = Run("-p", _tempDir, "-f", "json");
        Assert.Equal(0, exitCode);

        var doc = JsonNode.Parse(stdout)!;
        var assembly = doc["assemblies"]!.AsArray().FirstOrDefault();
        Assert.NotNull(assembly);
        Assert.NotNull(assembly!["metrics"]!["ecsTypeCount"]);
        Assert.NotNull(assembly["metrics"]!["burstCoverage"]);
    }

    [Fact]
    public void E2E_SelfAnalysis_NoEcsSmellsAndNullBurstCoverage()
    {
        var srcPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));
        var (exitCode, stdout, _) = Run("-p", srcPath, "-f", "json");
        Assert.Equal(0, exitCode);

        var doc = JsonNode.Parse(stdout)!;
        var burstValues = doc["assemblies"]!.AsArray()
            .Select(a => a!["metrics"]!["burstCoverage"])
            .ToList();
        Assert.All(burstValues, v => Assert.True(v is null || v.GetValueKind() == System.Text.Json.JsonValueKind.Null));

        var (sarifExit, sarifOut, _) = Run("-p", srcPath, "-f", "sarif");
        Assert.Equal(0, sarifExit);
        var sarif = JsonNode.Parse(sarifOut)!;
        var ecsRules = sarif["runs"]![0]!["results"]!
            .AsArray()
            .Select(r => r!["ruleId"]!.GetValue<string>())
            .Where(id => id is "UNI024" or "UNI025")
            .ToList();
        Assert.Empty(ecsRules);
    }

    void WriteEcsProject()
    {
        var scriptsDir = Path.Combine(_tempDir, "Assets", "Scripts");
        Directory.CreateDirectory(scriptsDir);
        File.WriteAllText(Path.Combine(scriptsDir, "Stubs.cs"), """
            namespace Unity.Entities {
                public interface ISystem { void OnCreate(ref SystemState s); void OnUpdate(ref SystemState s); }
                public interface IJobEntity { }
                public interface IComponentData { }
                public struct SystemState { }
            }
            namespace Burst { public class BurstCompileAttribute : System.Attribute { } }
            """);
        File.WriteAllText(Path.Combine(scriptsDir, "Offenders.cs"), """
            using Unity.Entities;
            partial struct BadSystem : ISystem {
                public void OnCreate(ref SystemState s) { }
                public void OnUpdate(ref SystemState s) { }
            }
            partial struct BadJob : IJobEntity { }
            struct BadComponent : IComponentData { public string Label; }
            [Burst.BurstCompile]
            partial struct GoodJob : IJobEntity { }
            """);
    }

    static void WriteCleanEcsProject(string dir)
    {
        var scriptsDir = Path.Combine(dir, "Assets", "Scripts");
        Directory.CreateDirectory(scriptsDir);
        File.WriteAllText(Path.Combine(scriptsDir, "Stubs.cs"), """
            namespace Unity.Entities {
                public interface ISystem { void OnCreate(ref SystemState s); void OnUpdate(ref SystemState s); }
                public interface IComponentData { }
                public struct SystemState { }
            }
            namespace Burst { public class BurstCompileAttribute : System.Attribute { } }
            """);
        File.WriteAllText(Path.Combine(scriptsDir, "Clean.cs"), """
            using Unity.Entities;
            [Burst.BurstCompile]
            partial struct GoodSystem : ISystem {
                public void OnCreate(ref SystemState s) { }
                public void OnUpdate(ref SystemState s) { }
            }
            class ManagedComponent : IComponentData { public string Label; }
            struct UnmanagedComponent : IComponentData { public int Value; }
            """);
    }

    static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = DotnetHostPath,
        };
        psi.ArgumentList.Add(AppDllPath);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        return TestProcessRunner.Run(psi, 60_000);
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
