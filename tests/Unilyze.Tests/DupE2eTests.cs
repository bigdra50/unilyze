using System.Text.Json;

namespace Unilyze.Tests;

public sealed class DupE2eTests : IDisposable
{
    readonly string _tempDir;

    public DupE2eTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-dup-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        WriteCloneFixture(_tempDir);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDir))
            return;
        foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_tempDir, true);
    }

    static void WriteCloneFixture(string root)
    {
        var scriptsDir = Path.Combine(root, "Assets", "Scripts");
        Directory.CreateDirectory(scriptsDir);
        var body = string.Join('\n', Enumerable.Range(0, 40).Select(i =>
            $"        buffer[{i}] = buffer[{i}] + {i % 7}; if (buffer[{i}] > cutoff) {{ accumulator += buffer[{i}]; }}"));
        var sharedMethod = $$"""
                public static int SharedWork(int[] buffer, int cutoff)
                {
                    var accumulator = 0;
            {{body}}
                    return accumulator;
                }
            """;
        File.WriteAllText(Path.Combine(scriptsDir, "CloneA.cs"), "namespace First;\npublic static class CloneSourceA\n{\n" + sharedMethod + "\n}\n");
        File.WriteAllText(Path.Combine(scriptsDir, "CloneB.cs"), "namespace Second;\npublic static class CloneSourceB\n{\n" + sharedMethod + "\n}\n");

        Directory.CreateDirectory(Path.Combine(root, "Assets", "Plugins"));
        File.WriteAllText(Path.Combine(root, "Assets", "Plugins", "VendorA.cs"), "namespace Vendor;\npublic static class VendorA\n{\n" + sharedMethod + "\n}\n");
        File.WriteAllText(Path.Combine(root, "Assets", "Plugins", "VendorB.cs"), "namespace Vendor;\npublic static class VendorB\n{\n" + sharedMethod + "\n}\n");
    }

    static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
        => CliE2eTestsHelper.Run(args);

    [Fact]
    public void DupJson_DetectsCrossFileClone()
    {
        var (exitCode, stdout, _) = Run("dup", "-p", _tempDir, "-f", "json");
        Assert.Equal(0, exitCode);

        var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.Equal(3, root.GetProperty("metricsVersion").GetInt32());
        Assert.True(root.GetProperty("summary").GetProperty("cloneClassCount").GetInt32() >= 1);

        var firstClass = root.GetProperty("cloneClasses")[0];
        Assert.Equal("DuplicatedCode", firstClass.GetProperty("findingKind").GetString());
        Assert.True(firstClass.GetProperty("occurrences").GetArrayLength() >= 2);
    }

    [Fact]
    public void BadgeDup_FailOverDecimal_ExitsTwoWhenOverThreshold()
    {
        var (_, stdout, _) = Run("dup", "-p", _tempDir, "-f", "json");
        var percent = JsonDocument.Parse(stdout).RootElement
            .GetProperty("summary").GetProperty("duplicationPercent").GetDouble();
        Assert.True(percent > 0);

        var (passExit, _, _) = Run("badge", "-p", _tempDir, "--metric", "dup", "--fail-over",
            (percent + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0, passExit);

        var (failExit, _, stderr) = Run("badge", "-p", _tempDir, "--metric", "dup", "--fail-over",
            (percent - 0.01).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(2, failExit);
        Assert.Contains("gate failed", stderr);
    }

    [Fact]
    public void BadgeDup_FailUnder_IsUsageError()
    {
        var (exitCode, _, stderr) = Run("badge", "-p", _tempDir, "--metric", "dup", "--fail-under", "3");
        Assert.Equal(1, exitCode);
        Assert.Contains("--fail-under is not valid with --metric dup", stderr);
    }

    [Fact]
    public void Dup_SuppressesSameThirdPartyPairsByDefault()
    {
        var (exitCode, stdout, _) = Run("dup", "-p", _tempDir, "-f", "json");
        Assert.Equal(0, exitCode);
        var summary = JsonDocument.Parse(stdout).RootElement.GetProperty("summary");
        Assert.True(summary.GetProperty("suppressedPairCount").GetInt32() >= 1);
    }
}
