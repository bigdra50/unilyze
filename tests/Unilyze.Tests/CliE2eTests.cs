using System.Diagnostics;
using System.Text.Json;

namespace Unilyze.Tests;

public sealed class CliE2eTests : IDisposable
{
    private readonly string _tempDir;
    private static readonly string ProjectPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Unilyze", "Unilyze.csproj"));

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
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(ProjectPath);
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("net8.0");
        psi.ArgumentList.Add("--no-build");
        psi.ArgumentList.Add("--");
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(60_000);
        return (proc.ExitCode, stdout, stderr);
    }

    [Fact]
    public void Help_ExitsZero()
    {
        var (exitCode, stdout, _) = Run("--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("unilyze", stdout);
        Assert.Contains("Usage:", stdout);
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
