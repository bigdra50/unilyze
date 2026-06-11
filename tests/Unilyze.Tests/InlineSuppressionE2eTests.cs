using System.Diagnostics;
using System.Text.Json;

namespace Unilyze.Tests;

public sealed class InlineSuppressionE2eTests : IDisposable
{
    readonly string _tempDir;

    public InlineSuppressionE2eTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"unilyze-inline-e2e-{Guid.NewGuid():N}");
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

    [Fact]
    public void E2e_DisableNextLine_SuppressesTargetSmellAndPassesBadgeGate()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Sample.cs"), """
            public class Sample
            {
                void Guarded()
                {
                    try { System.Console.WriteLine(); }
                    // unilyze-disable-next-line UNI014 -- top-level guard, intentional
                    catch { }
                }

                void Reported()
                {
                    try { System.Console.WriteLine(); }
                    catch { }
                }
            }
            """);

        var (analyzeExit, stdout, analyzeErr) = Run("-p", _tempDir, "-f", "json");
        Assert.Equal(0, analyzeExit);
        Assert.Contains("suppressed by inline comments", analyzeErr, StringComparison.OrdinalIgnoreCase);

        var doc = JsonDocument.Parse(stdout);
        Assert.Equal(1, doc.RootElement.GetProperty("suppressedCount").GetInt32());

        var kinds = doc.RootElement.GetProperty("typeMetrics").EnumerateArray()
            .SelectMany(type => type.GetProperty("codeSmells").EnumerateArray())
            .GroupBy(smell => smell.TryGetProperty("suppressed", out var suppressed) && suppressed.GetBoolean())
            .ToDictionary(g => g.Key, g => g.Select(x => x.GetProperty("kind").GetString()).ToList());

        Assert.Single(kinds[true]);
        Assert.Equal("CatchAllException", kinds[true][0]);
        Assert.Single(kinds[false]);
        Assert.Equal("CatchAllException", kinds[false][0]);

        var (sarifExit, sarifOut, _) = Run("-p", _tempDir, "-f", "sarif");
        Assert.Equal(0, sarifExit);
        using var sarifDoc = JsonDocument.Parse(sarifOut);
        var kindsInSarif = sarifDoc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results")
            .EnumerateArray()
            .Where(r => r.TryGetProperty("suppressions", out _))
            .Select(r => r.GetProperty("suppressions")[0].GetProperty("kind").GetString())
            .Distinct()
            .ToList();
        Assert.Equal(["inSource"], kindsInSarif);

        var (badgeExit, _, _) = Run("badge", "-p", _tempDir, "--metric", "smells", "--fail-over", "1");
        Assert.Equal(0, badgeExit);
    }

    [Fact]
    public void E2e_UnknownRuleId_WarnsWithoutChangingExitCode()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Sample.cs"), """
            public class Sample
            {
                // unilyze-disable UNI999
                void M() { }
            }
            """);

        var (exitCode, _, stderr) = Run("-p", _tempDir, "-f", "json");
        Assert.Equal(0, exitCode);
        Assert.Contains("Unknown rule id 'UNI999'", stderr);
    }

    static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
        => CliE2eTestsHelper.Run(args);
}
