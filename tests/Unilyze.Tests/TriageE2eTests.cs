using System.Text.Json;

namespace Unilyze.Tests;

public sealed class TriageE2eTests : IDisposable
{
    readonly string _tempDir;

    public TriageE2eTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-triage-e2e-{Guid.NewGuid():N}");
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
    public void Triage_SetThenAnalyze_AppliesFalsePositive()
    {
        WriteProjectWithLongMethod(_tempDir, "Run");
        var triagePath = Path.Combine(_tempDir, ".unilyze", "triage.json");

        var (analyzeExit, preStdout, _) = Run("-p", _tempDir, "-f", "json");
        Assert.Equal(0, analyzeExit);
        var preDoc = JsonDocument.Parse(preStdout);
        var id = preDoc.RootElement
            .GetProperty("typeMetrics")
            .EnumerateArray()
            .SelectMany(type => type.GetProperty("codeSmells").EnumerateArray())
            .First(smell => smell.GetProperty("severity").GetString() == "Warning")
            .GetProperty("id")
            .GetString();
        Assert.False(string.IsNullOrEmpty(id));

        var (setExit, _, setErr) = Run(
            "triage", "set", id!, TriageVerdicts.FalsePositive, "--reason", "verified", "-p", _tempDir);
        Assert.Equal(0, setExit);
        Assert.Contains("Written to", setErr);
        Assert.True(File.Exists(triagePath));

        var (postExit, postStdout, postErr) = Run("-p", _tempDir, "-f", "json");
        Assert.Equal(0, postExit);
        Assert.Contains("false-positive=1", postErr);

        var triagedCount = JsonDocument.Parse(postStdout).RootElement
            .GetProperty("typeMetrics")
            .EnumerateArray()
            .SelectMany(type => type.GetProperty("codeSmells").EnumerateArray())
            .Count(smell => smell.TryGetProperty("triage", out var triage)
                && triage.GetString() == TriageVerdicts.FalsePositive);
        Assert.Equal(1, triagedCount);
    }

    [Fact]
    public void Triage_UnknownVerdict_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("triage", "set", "abc", "bogus", "-p", _tempDir);
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown verdict", stderr);
    }

    [Fact]
    public void Triage_HelpListedInTopLevelHelp()
    {
        var (exitCode, stdout, _) = Run("--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("triage", stdout);
    }

    static void WriteProjectWithLongMethod(string tempDir, string methodName)
    {
        var body = string.Join("\n", Enumerable.Range(1, 85).Select(i => $"        x += {i};"));
        File.WriteAllText(Path.Combine(tempDir, "Sample.cs"), $$"""
            namespace Sample;

            public class SmellyType
            {
                public int {{methodName}}(int seed)
                {
                    var x = seed;
            {{body}}
                    return x;
                }
            }
            """);
    }
}
