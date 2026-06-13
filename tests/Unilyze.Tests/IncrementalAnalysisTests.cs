using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Unilyze.Tests.Helpers;

namespace Unilyze.Tests;

public sealed class IncrementalAnalysisTests : IDisposable
{
    readonly string _projectRoot;
    static readonly string CurrentTargetFramework = ResolveCurrentTargetFramework();
    static readonly string DotnetHostPath = ResolveDotnetHostPath();
    static readonly string AppDllPath = ResolveAppDllPath();

    public IncrementalAnalysisTests()
    {
        _projectRoot = Path.Combine(Path.GetTempPath(), $"unilyze-inc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectRoot);
        WriteInitialProject();
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectRoot))
            Directory.Delete(_projectRoot, true);
    }

    [Fact]
    public void WarmIncremental_MatchesFullSyntaxRun()
    {
        var baseline = Analyze(fullIncremental: false);
        Analyze(fullIncremental: true); // cold cache build
        var warm = Analyze(fullIncremental: true);

        Assert.Equal(Normalize(baseline), Normalize(warm));
    }

    [Fact]
    public void WithoutIncremental_DoesNotCreateCache()
    {
        Analyze(fullIncremental: false);
        Assert.False(Directory.Exists(SyntaxCacheStore.GetCacheDirectory(_projectRoot)));
    }

    [Fact]
    public void IncrementalWithInput_IsUsageError()
    {
        var (exitCode, _, stderr) = Run("-i", "missing.json", "--incremental");
        Assert.Equal(1, exitCode);
        Assert.Contains("--incremental cannot be combined", stderr);
    }

    [Fact]
    public void IncrementalWithoutSyntaxLevel_WarnsAndIgnoresCache()
    {
        var (exitCode, _, stderr) = Run("-p", _projectRoot, "--incremental", "-f", "json");
        Assert.Equal(0, exitCode);
        Assert.Contains("syntax-level analysis only", stderr);
        Assert.False(Directory.Exists(SyntaxCacheStore.GetCacheDirectory(_projectRoot)));
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("add")]
    [InlineData("delete")]
    [InlineData("partial")]
    [InlineData("interface-flip")]
    [InlineData("threshold")]
    [InlineData("define")]
    public void Mutations_StayEquivalentToFullRun(string mutation)
    {
        Analyze(fullIncremental: true); // seed cache
        ApplyMutation(mutation);
        var incremental = Analyze(fullIncremental: true);
        var full = Analyze(fullIncremental: false);
        Assert.Equal(Normalize(full), Normalize(incremental));
    }

    [Fact]
    public async Task ConcurrentIncrementalRuns_DoNotCorruptCache()
    {
        Analyze(fullIncremental: true);

        var psi = new ProcessStartInfo
        {
            FileName = DotnetHostPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(AppDllPath);
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(_projectRoot);
        psi.ArgumentList.Add("--level");
        psi.ArgumentList.Add("syntax");
        psi.ArgumentList.Add("--incremental");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("json");

        using var first = Process.Start(psi)!;
        using var second = Process.Start(psi)!;
        var firstStdoutTask = first.StandardOutput.ReadToEndAsync();
        var firstStderrTask = first.StandardError.ReadToEndAsync();
        var secondStdoutTask = second.StandardOutput.ReadToEndAsync();
        var secondStderrTask = second.StandardError.ReadToEndAsync();

        var (_, firstStderr) = await WaitForExit(first, firstStdoutTask, firstStderrTask, "first");
        var (_, secondStderr) = await WaitForExit(second, secondStdoutTask, secondStderrTask, "second");
        if (first.ExitCode != 0)
            Assert.Fail($"First incremental analysis exited with code {first.ExitCode}. stderr:{Environment.NewLine}{firstStderr}");
        if (second.ExitCode != 0)
            Assert.Fail($"Second incremental analysis exited with code {second.ExitCode}. stderr:{Environment.NewLine}{secondStderr}");
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);

        var full = Analyze(fullIncremental: false);
        var warm = Analyze(fullIncremental: true);
        Assert.Equal(Normalize(full), Normalize(warm));
    }

    static async Task<(string StdOut, string StdErr)> WaitForExit(
        Process process,
        Task<string> stdoutTask,
        Task<string> stderrTask,
        string processName)
    {
        if (!process.WaitForExit(120_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup; the timeout assertion below must still report diagnostics.
            }

            string stderr;
            try
            {
                stderr = await stderrTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                stderr = "<stderr drain did not complete after termination>";
            }

            Assert.Fail($"{processName} incremental analysis timed out. stderr:{Environment.NewLine}{stderr}");
        }

        return (await stdoutTask, await stderrTask);
    }

    [Fact]
    public void SelfAnalysis_WarmRunIsFasterThanColdSyntaxRun()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var cacheDir = SyntaxCacheStore.GetCacheDirectory(repoRoot);
        if (Directory.Exists(cacheDir))
            Directory.Delete(cacheDir, true);

        var coldMs = MeasureRun(repoRoot, incremental: false);
        var firstIncrementalMs = MeasureRun(repoRoot, incremental: true);
        var warmMs = MeasureRun(repoRoot, incremental: true);

        Assert.True(warmMs <= firstIncrementalMs,
            $"Expected warm ({warmMs}ms) <= first incremental ({firstIncrementalMs}ms)");
        Assert.True(warmMs < coldMs,
            $"Expected warm ({warmMs}ms) < cold full ({coldMs}ms)");
    }

    void WriteInitialProject()
    {
        File.WriteAllText(Path.Combine(_projectRoot, "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "Alpha.cs"), """
            namespace Sample;

            public class Alpha
            {
                public int Value { get; set; }
            }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "Beta.cs"), """
            namespace Sample;

            public partial class Beta
            {
                public void PartOne() { }
            }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "Beta.Part.cs"), """
            namespace Sample;

            public partial class Beta
            {
                public void PartTwo() { }
            }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "Gamma.cs"), """
            namespace Sample;

            public class Gamma : Delta { }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "Delta.cs"), """
            namespace Sample;

            public class Delta { }
            """);
    }

    void ApplyMutation(string mutation)
    {
        switch (mutation)
        {
            case "edit":
                File.AppendAllText(Path.Combine(_projectRoot, "Alpha.cs"), "\n// touch\n");
                break;
            case "add":
                File.WriteAllText(Path.Combine(_projectRoot, "Added.cs"), """
                    namespace Sample;
                    public class Added { }
                    """);
                break;
            case "delete":
                File.Delete(Path.Combine(_projectRoot, "Gamma.cs"));
                break;
            case "partial":
                File.AppendAllText(Path.Combine(_projectRoot, "Beta.Part.cs"), "\n// partial touch\n");
                break;
            case "interface-flip":
                File.WriteAllText(Path.Combine(_projectRoot, "Delta.cs"), """
                    namespace Sample;
                    public interface Delta { }
                    """);
                break;
            case "threshold":
                File.WriteAllText(Path.Combine(_projectRoot, ".unilyze.json"), """
                    { "smells": { "godClass": { "linesWarning": 9999 } } }
                    """);
                break;
            case "define":
                File.WriteAllText(Path.Combine(_projectRoot, "App.csproj"), """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net8.0</TargetFramework>
                        <DefineConstants>INCREMENTAL_TEST</DefineConstants>
                      </PropertyGroup>
                    </Project>
                    """);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    string Analyze(bool fullIncremental)
    {
        var args = new List<string> { "-p", _projectRoot, "--level", "syntax", "-f", "json" };
        if (fullIncremental)
            args.Add("--incremental");
        var (exitCode, stdout, stderr) = Run(args.ToArray());
        Assert.Equal(0, exitCode);
        return stdout;
    }

    static long MeasureRun(string projectRoot, bool incremental)
    {
        var args = new List<string>
        {
            "-p", projectRoot, "--level", "syntax", "-f", "json", "-o", Path.GetTempFileName()
        };
        if (incremental)
            args.Add("--incremental");

        var sw = Stopwatch.StartNew();
        var (exitCode, _, _) = Run(args.ToArray());
        sw.Stop();
        Assert.Equal(0, exitCode);
        return sw.ElapsedMilliseconds;
    }

    static string Normalize(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Invalid JSON");
        root.Remove("analyzedAt");
        root.Remove("toolVersion");
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
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

        return TestProcessRunner.Run(psi, 120_000);
    }

    static string ResolveCurrentTargetFramework()
    {
        var tfm = Path.GetFileName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        return tfm!;
    }

    static string ResolveDotnetHostPath() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    static string ResolveAppDllPath()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Unilyze", "bin", "Release", CurrentTargetFramework, "Unilyze.dll"));
        if (!File.Exists(path))
        {
            path = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "Unilyze", "bin", "Debug", CurrentTargetFramework, "Unilyze.dll"));
        }
        return path;
    }
}
