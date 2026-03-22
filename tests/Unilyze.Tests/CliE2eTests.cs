using System.Diagnostics;
using System.Text.Json;

namespace Unilyze.Tests;

public sealed class CliE2eTests : IDisposable
{
    private readonly string _tempDir;
    private static readonly string CurrentTargetFramework = ResolveCurrentTargetFramework();
    private static readonly string DotnetHostPath = ResolveDotnetHostPath();
    private static readonly string AppDllPath = ResolveAppDllPath();

    public CliE2eTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            // Reuse the SDK-selected host to avoid apphost/runtime lookup mismatches in CI and local dev.
            FileName = DotnetHostPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(AppDllPath);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {DotnetHostPath}");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(60_000);
        return (proc.ExitCode, stdout, stderr);
    }

    private static string ResolveCurrentTargetFramework()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var tfm = Path.GetFileName(baseDir);
        if (string.IsNullOrWhiteSpace(tfm) || !tfm.StartsWith("net", StringComparison.Ordinal))
            throw new InvalidOperationException($"Could not infer target framework from base directory: {AppContext.BaseDirectory}");
        return tfm;
    }

    private static string ResolveDotnetHostPath()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return string.IsNullOrWhiteSpace(configured) ? "dotnet" : configured;
    }

    private static string ResolveAppDllPath()
    {
        var path = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Unilyze", "bin", "Debug", CurrentTargetFramework, "Unilyze.dll"));

        if (!File.Exists(path))
            throw new FileNotFoundException($"Could not find CLI assembly under test: {path}", path);

        return path;
    }

    [Fact]
    public void Help_ExitsZero()
    {
        var (exitCode, stdout, _) = Run("--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("unilyze", stdout);
        Assert.Contains("Usage:", stdout);
        Assert.Contains("--no-open", stdout);
    }

    [Fact]
    public void Version_ExitsZero()
    {
        var (exitCode, stdout, _) = Run("--version");
        Assert.Equal(0, exitCode);
        Assert.StartsWith("unilyze ", stdout.Trim());
    }

    [Fact]
    public void JsonFormat_OutputsValidJson()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("-p", _tempDir, "-f", "json");
        Assert.Equal(0, exitCode);
        var doc = JsonDocument.Parse(stdout);
        Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("projectPath").ValueKind);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("assemblies").ValueKind);
    }

    [Fact]
    public void SarifFormat_OutputsValidSarif()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("-p", _tempDir, "-f", "sarif");
        Assert.Equal(0, exitCode);
        var doc = JsonDocument.Parse(stdout);
        var schema = doc.RootElement.GetProperty("$schema").GetString();
        Assert.Contains("sarif", schema, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidFormat_ExitsNonZero()
    {
        var (exitCode, _, stderr) = Run("-p", _tempDir, "-f", "csv");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown format", stderr);
    }

    [Fact]
    public void NonExistentPath_ExitsNonZero()
    {
        var fakePath = Path.Combine(_tempDir, "does-not-exist");
        var (exitCode, _, _) = Run("-p", fakePath, "-f", "json");
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void HtmlFormat_NoOpen_WritesArtifactsWithoutLaunchingBrowser()
    {
        WriteSimpleProject();
        var (exitCode, _, stderr) = Run("-p", _tempDir, "--no-open");

        Assert.Equal(0, exitCode);
        var writtenLines = stderr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("Written to ", StringComparison.Ordinal))
            .ToList();
        Assert.True(writtenLines.Count >= 2, stderr);

        foreach (var line in writtenLines)
        {
            var path = line["Written to ".Length..];
            Assert.True(File.Exists(path), $"Expected artifact to exist: {path}");
        }
    }

    [Fact]
    public void InvalidJsonInput_ExitsNonZeroWithFriendlyMessage()
    {
        var invalidJson = Path.Combine(_tempDir, "invalid.json");
        File.WriteAllText(invalidJson, "{ this is not valid json }");

        var (exitCode, _, stderr) = Run("-i", invalidJson, "-f", "json");

        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid JSON input", stderr);
    }

    [Fact]
    public void DiffSubcommand_Help_ExitsZero()
    {
        var (exitCode, stdout, _) = Run("diff", "--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("diff", stdout);
    }

    [Fact]
    public void DiffSubcommand_WithJsonFiles_ProducesDiff()
    {
        var result = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], []);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var beforeFile = Path.Combine(_tempDir, "before.json");
        var afterFile = Path.Combine(_tempDir, "after.json");
        File.WriteAllText(beforeFile, json);
        File.WriteAllText(afterFile, json);

        var (exitCode, stdout, _) = Run("diff", beforeFile, afterFile);
        Assert.Equal(0, exitCode);
        var doc = JsonDocument.Parse(stdout);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("summary").ValueKind);
    }

    [Fact]
    public void Statusline_Help_ExitsZero()
    {
        var (exitCode, stdout, _) = Run("statusline", "--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("statusline", stdout);
    }

    [Fact]
    public void Statusline_AnalyzesProject_OutputsFormattedLine()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("statusline", "-p", _tempDir);
        Assert.Equal(0, exitCode);
        Assert.Contains("CH:", stdout);
    }

    private void WriteSimpleProject()
    {
        var csFile = Path.Combine(_tempDir, "Sample.cs");
        File.WriteAllText(csFile, """
            namespace Sample;
            public class Greeter
            {
                public string Greet(string name) => $"Hello, {name}!";
            }
            """);
    }
}
