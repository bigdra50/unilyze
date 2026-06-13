using System.Diagnostics;
using System.Text.Json;
using Unilyze.Tests.Helpers;

namespace Unilyze.Tests;

public sealed class BaselineE2eTests : IDisposable
{
    readonly string _tempDir;
    static readonly string CurrentTargetFramework = CliE2eTestsHelper.CurrentTargetFramework;
    static readonly string DotnetHostPath = CliE2eTestsHelper.DotnetHostPath;
    static readonly string AppDllPath = CliE2eTestsHelper.AppDllPath;

    public BaselineE2eTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-baseline-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDir))
            return;

        foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_tempDir, true);
    }

    static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
        => CliE2eTestsHelper.Run(args);

    [Fact]
    public void Baseline_CreateThenAnalyze_ReportsZeroNewSmells()
    {
        WriteProjectWithLongMethod(_tempDir, "OriginalLongMethod");
        var baselinePath = Path.Combine(_tempDir, "baseline.json");

        var (createExit, _, createErr) = Run("baseline", "create", "-p", _tempDir, "-o", baselinePath);
        Assert.Equal(0, createExit);
        Assert.Contains("Written to", createErr);
        Assert.True(File.Exists(baselinePath));

        var (analyzeExit, stdout, analyzeErr) = Run("-p", _tempDir, "-f", "json", "--baseline", baselinePath);
        Assert.Equal(0, analyzeExit);
        Assert.Contains("suppressed", analyzeErr, StringComparison.OrdinalIgnoreCase);

        var doc = JsonDocument.Parse(stdout);
        Assert.True(doc.RootElement.TryGetProperty("suppressedCount", out var suppressed));
        Assert.True(suppressed.GetInt32() > 0);

        var baselinedSmells = doc.RootElement
            .GetProperty("typeMetrics")
            .EnumerateArray()
            .SelectMany(type => type.GetProperty("codeSmells").EnumerateArray())
            .Where(smell => smell.TryGetProperty("baselined", out var baselined) && baselined.GetBoolean())
            .ToList();
        Assert.NotEmpty(baselinedSmells);

        var unsuppressed = doc.RootElement
            .GetProperty("typeMetrics")
            .EnumerateArray()
            .SelectMany(type => type.TryGetProperty("codeSmells", out var smells)
                ? smells.EnumerateArray()
                : [])
            .Count(smell => !smell.TryGetProperty("baselined", out var baselined) || !baselined.GetBoolean());
        Assert.Equal(0, unsuppressed);
    }

    [Fact]
    public void Baseline_NewSmellAfterCreate_ReportsOnlyNewViolation()
    {
        WriteProjectWithLongMethod(_tempDir, "OriginalLongMethod");
        var baselinePath = Path.Combine(_tempDir, "baseline.json");

        var (createExit, _, createErr) = Run("baseline", "create", "-p", _tempDir, "-o", baselinePath);
        Assert.Equal(0, createExit);
        Assert.Contains("Written to", createErr);
        Assert.True(File.Exists(baselinePath));

        AppendProjectWithLongMethod(_tempDir, "AddedType", "AddedLongMethod");

        var (analyzeExit, stdout, _) = Run("-p", _tempDir, "-f", "json", "--baseline", baselinePath);
        Assert.Equal(0, analyzeExit);

        var newSmells = docSmellsWithoutBaseline(stdout);
        var newLongMethods = newSmells
            .Where(smell => smell.GetProperty("kind").GetString() == "LongMethod")
            .ToList();
        Assert.Single(newLongMethods);
        Assert.Equal("AddedLongMethod", newLongMethods[0].GetProperty("methodName").GetString());

        var (badgePassExit, _, _) = Run(
            "badge", "-p", _tempDir, "--metric", "smells", "--fail-over", "0", "--baseline", baselinePath);
        Assert.Equal(2, badgePassExit);
    }

    [Fact]
    public void Baseline_BadgeGate_PassesImmediatelyAfterCreate()
    {
        WriteProjectWithLongMethod(_tempDir, "OriginalLongMethod");
        var baselinePath = Path.Combine(_tempDir, "baseline.json");
        Assert.Equal(0, Run("baseline", "create", "-p", _tempDir, "-o", baselinePath).ExitCode);

        var (exitCode, _, _) = Run(
            "badge", "-p", _tempDir, "--metric", "smells", "--fail-over", "0", "--baseline", baselinePath);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Baseline_MissingFile_ExitsUsageError()
    {
        WriteProjectWithLongMethod(_tempDir, "OriginalLongMethod");
        var (exitCode, _, stderr) = Run("-p", _tempDir, "-f", "json", "--baseline", "missing.json");
        Assert.Equal(1, exitCode);
        Assert.Contains("Baseline file not found", stderr);
    }

    [Fact]
    public void Baseline_HelpListedInTopLevelHelp()
    {
        var (exitCode, stdout, _) = Run("--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("baseline", stdout);
    }

    static List<JsonElement> docSmellsWithoutBaseline(string json)
    {
        var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("typeMetrics")
            .EnumerateArray()
            .SelectMany(type => type.TryGetProperty("codeSmells", out var smells)
                ? smells.EnumerateArray()
                : [])
            .Where(smell => !smell.TryGetProperty("baselined", out var baselined) || !baselined.GetBoolean())
            .ToList();
    }

    static void WriteProjectWithLongMethod(string tempDir, params string[] methodNames)
    {
        var methods = string.Join("\n\n", methodNames.Select(BuildLongMethod));
        var csFile = Path.Combine(tempDir, "Sample.cs");
        File.WriteAllText(csFile, $$"""
            namespace Sample;

            public class SmellyType
            {
            {{methods}}
            }
            """);
    }

    static void AppendProjectWithLongMethod(string tempDir, string typeName, string methodName)
    {
        var csFile = Path.Combine(tempDir, $"{typeName}.cs");
        File.WriteAllText(csFile, $$"""
            namespace Sample;

            public class {{typeName}}
            {
            {{BuildLongMethod(methodName)}}
            }
            """);
    }

    static string BuildLongMethod(string methodName)
    {
        var body = string.Join("\n", Enumerable.Range(1, 85).Select(i => $"        x += {i};"));
        return $$"""
                public int {{methodName}}(int seed)
                {
                    var x = seed;
            {{body}}
                    return x;
                }
            """;
    }
}

internal static class CliE2eTestsHelper
{
    internal static readonly string CurrentTargetFramework = ResolveCurrentTargetFramework();
    internal static readonly string DotnetHostPath = ResolveDotnetHostPath();
    internal static readonly string AppDllPath = ResolveAppDllPath();

    internal static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
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

    internal static (int ExitCode, string StdOut, string StdErr) RunWithInput(string stdin, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = DotnetHostPath,
        };
        psi.ArgumentList.Add(AppDllPath);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        return TestProcessRunner.RunWithStdin(psi, stdin, 120_000);
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
