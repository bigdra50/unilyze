using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Unilyze.Tests.Helpers;

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
        if (!Directory.Exists(_tempDir))
            return;

        // git marks object files read-only; Windows refuses to recursively delete them.
        foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_tempDir, true);
    }

    private static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            // Reuse the SDK-selected host to avoid apphost/runtime lookup mismatches in CI and local dev.
            FileName = DotnetHostPath,
        };
        psi.ArgumentList.Add(AppDllPath);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        return TestProcessRunner.Run(psi, 60_000);
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
    public void Level_Syntax_PinsAndReportsSyntaxOnly()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("-p", _tempDir, "-f", "json", "--level", "syntax");
        Assert.Equal(0, exitCode);

        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal("SyntaxOnly", root.GetProperty("analysisLevel").GetString());
    }

    [Fact]
    public void Level_Syntax_CapsBelowCsprojResolvedLevel()
    {
        // A .csproj with a resolvable <Reference> auto-elevates the analysis to
        // CoreEngine, so this is the decisive cap-down case: the same project
        // pinned to syntax must stay SyntaxOnly instead of being re-elevated.
        WriteSimpleProject();
        WriteCsprojWithValidReference();

        var (unpinnedExit, unpinnedStdout, _) = Run("-p", _tempDir, "-f", "json");
        Assert.Equal(0, unpinnedExit);
        var unpinnedRoot = JsonDocument.Parse(unpinnedStdout).RootElement;
        Assert.Equal("Complete", unpinnedRoot.GetProperty("analysisLevel").GetString());

        var (pinnedExit, pinnedStdout, _) = Run("-p", _tempDir, "-f", "json", "--level", "syntax");
        Assert.Equal(0, pinnedExit);
        var pinnedRoot = JsonDocument.Parse(pinnedStdout).RootElement;
        Assert.Equal("SyntaxOnly", pinnedRoot.GetProperty("analysisLevel").GetString());
    }

    [Fact]
    public void Level_Complete_OnNonUnityProject_ExitsZero()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("-p", _tempDir, "-f", "json", "--level", "complete");
        Assert.Equal(0, exitCode);

        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal("Complete", root.GetProperty("analysisLevel").GetString());
    }

    [Fact]
    public void Level_Unknown_ExitsNonZero()
    {
        var (exitCode, _, stderr) = Run("-p", _tempDir, "-f", "json", "--level", "bogus");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown level", stderr);
    }

    [Fact]
    public void JsonOutput_IncludesAnalysisLevel()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("-p", _tempDir, "-f", "json");
        Assert.Equal(0, exitCode);

        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.True(root.TryGetProperty("analysisLevel", out var level));
        Assert.Equal("Complete", level.GetString());
    }

    [Fact]
    public void JsonOutput_IncludesMetricsVersionAndToolVersion()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("-p", _tempDir, "-f", "json");
        Assert.Equal(0, exitCode);

        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal(AnalysisResult.CurrentMetricsVersion, root.GetProperty("metricsVersion").GetInt32());
        Assert.Equal(JsonValueKind.String, root.GetProperty("toolVersion").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("toolVersion").GetString()));
    }

    [Fact]
    public void DiffSubcommand_MismatchedMetricsVersions_WarnsOnStderr()
    {
        var before = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], MetricsVersion: 1);
        var after = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], MetricsVersion: 2);
        var beforeJson = JsonSerializer.Serialize(before, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var afterJson = JsonSerializer.Serialize(after, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var beforeFile = Path.Combine(_tempDir, "before.json");
        var afterFile = Path.Combine(_tempDir, "after.json");
        File.WriteAllText(beforeFile, beforeJson);
        File.WriteAllText(afterFile, afterJson);

        var (exitCode, _, stderr) = Run("diff", beforeFile, afterFile);
        Assert.Equal(0, exitCode);
        Assert.Contains("metrics versions differ", stderr);
    }

    [Fact]
    public void DiffSubcommand_SameMetricsVersions_NoVersionWarning()
    {
        var result = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], MetricsVersion: AnalysisResult.CurrentMetricsVersion);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var beforeFile = Path.Combine(_tempDir, "before.json");
        var afterFile = Path.Combine(_tempDir, "after.json");
        File.WriteAllText(beforeFile, json);
        File.WriteAllText(afterFile, json);

        var (exitCode, _, stderr) = Run("diff", beforeFile, afterFile);
        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("metrics versions differ", stderr);
    }

    [Fact]
    public void DiffSubcommand_FailOnVersionMismatch_ExitsTwo()
    {
        var before = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], MetricsVersion: 1);
        var after = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], MetricsVersion: 2);
        var beforeJson = JsonSerializer.Serialize(before, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var afterJson = JsonSerializer.Serialize(after, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var beforeFile = Path.Combine(_tempDir, "before.json");
        var afterFile = Path.Combine(_tempDir, "after.json");
        File.WriteAllText(beforeFile, beforeJson);
        File.WriteAllText(afterFile, afterJson);

        var (exitCode, _, stderr) = Run("diff", beforeFile, afterFile, "--fail-on-version-mismatch");
        Assert.Equal(2, exitCode);
        Assert.Contains("metrics versions differ", stderr);
    }

    [Fact]
    public void Badge_Help_MentionsLevel()
    {
        var (exitCode, stdout, _) = Run("badge", "--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("--level", stdout);
    }

    [Fact]
    public void Badge_JsonOutput_IncludesAnalysisLevel()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("badge", "-p", _tempDir);
        Assert.Equal(0, exitCode);

        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal("Complete", root.GetProperty("analysisLevel").GetString());
    }

    [Fact]
    public void Badge_LevelComplete_OnNonUnityProject_ExitsZero()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("badge", "-p", _tempDir, "--level", "complete");
        Assert.Equal(0, exitCode);

        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal("Complete", root.GetProperty("analysisLevel").GetString());
    }

    [Fact]
    public void Statusline_Help_MentionsLevel()
    {
        var (exitCode, stdout, _) = Run("statusline", "--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("--level", stdout);
    }

    [Fact]
    public void Statusline_DotnetProject_ShowsNoSyntaxMarker()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("statusline", "-p", _tempDir);
        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("[syntax]", stdout);
    }

    [Fact]
    public void Statusline_SyntaxPinnedProject_ShowsLevelMarker()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("statusline", "-p", _tempDir, "--level", "syntax");
        Assert.Equal(0, exitCode);
        Assert.Contains("[syntax]", stdout);
    }

    [Fact]
    public void Statusline_Help_MentionsVerboseAndQuiet()
    {
        var (exitCode, stdout, _) = Run("statusline", "--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("--verbose", stdout);
        Assert.Contains("--quiet", stdout);
    }

    [Fact]
    public void Statusline_Help_MentionsBackgroundRefresh()
    {
        var (exitCode, stdout, _) = Run("statusline", "--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("--background-refresh", stdout);
    }

    [Fact]
    public void Statusline_BackgroundRefreshFlag_IsAccepted()
    {
        WriteSimpleProject();
        var (exitCode, _, _) = Run("statusline", "-p", _tempDir, "--background-refresh");
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Statusline_BackgroundRefresh_ColdStart_ExitsQuicklyAndCreatesCache()
    {
        WriteSimpleProject();
        var cachePath = ResolveStatuslineCachePath(_tempDir);
        TryDeleteStatuslineCache(cachePath);

        var sw = Stopwatch.StartNew();
        var (exitCode, stdout, _) = Run("statusline", "-p", _tempDir, "--background-refresh");
        sw.Stop();

        Assert.Equal(0, exitCode);
        Assert.True(string.IsNullOrEmpty(stdout));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Expected immediate return, took {sw.Elapsed.TotalSeconds:F1}s");

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(cachePath) && File.ReadAllText(cachePath).Contains("CH:"))
                return;
            Thread.Sleep(250);
        }

        Assert.True(File.Exists(cachePath), $"Expected cache file at {cachePath}");
        Assert.Contains("CH:", File.ReadAllText(cachePath));
    }

    [Fact]
    public void Statusline_BackgroundRefresh_StaleCache_PrintsStaleAndRefreshesInBackground()
    {
        WriteSimpleProject();
        var cachePath = ResolveStatuslineCachePath(_tempDir);
        const string staleContent = "CH:STALE/1.0 0smells";
        File.WriteAllText(cachePath, staleContent);
        File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddHours(-2));

        var beforeMtime = File.GetLastWriteTimeUtc(cachePath);
        var sw = Stopwatch.StartNew();
        var (exitCode, stdout, _) = Run(
            "statusline", "-p", _tempDir, "--background-refresh", "--refresh", "3600");
        sw.Stop();

        Assert.Equal(0, exitCode);
        Assert.Equal(staleContent, stdout);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Expected immediate return, took {sw.Elapsed.TotalSeconds:F1}s");

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (File.GetLastWriteTimeUtc(cachePath) > beforeMtime
                && File.ReadAllText(cachePath) != staleContent)
                return;
            Thread.Sleep(250);
        }

        Assert.True(File.GetLastWriteTimeUtc(cachePath) > beforeMtime, "Expected background refresh to update cache mtime");
        Assert.NotEqual(staleContent, File.ReadAllText(cachePath));
        Assert.Contains("CH:", File.ReadAllText(cachePath));
    }

    [Fact]
    public void Statusline_VerboseNonexistentPath_NoCache_ExitsOneWithExceptionDetail()
    {
        var missingPath = Path.Combine(_tempDir, "does-not-exist");
        var (exitCode, stdout, stderr) = Run("statusline", "-p", missingPath, "--verbose");
        Assert.Equal(1, exitCode);
        Assert.True(string.IsNullOrEmpty(stdout) || !stdout.Contains("CH:"));
        Assert.False(string.IsNullOrWhiteSpace(stderr));
        Assert.Contains("Exception", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Statusline_TypoVerboseFlag_ExitsUsageErrorWithSuggestion()
    {
        WriteSimpleProject();
        var (exitCode, _, stderr) = Run("statusline", "-p", _tempDir, "--verbos", "--quiet");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option: '--verbos'", stderr);
        Assert.Contains("Did you mean '--verbose'?", stderr);
    }

    [Fact]
    public void Statusline_RedirectedStderr_EmitsNoPhaseProgress()
    {
        WriteSimpleProject();
        var (exitCode, _, stderr) = Run("statusline", "-p", _tempDir, "--refresh", "0");
        Assert.Equal(0, exitCode);
        Assert.DoesNotContain(" done ", stderr);
    }

    [Fact]
    public void DiffSubcommand_MismatchedLevels_WarnsOnStderr()
    {
        var before = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], null, "Complete");
        var after = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], null, "SyntaxOnly");
        var beforeJson = JsonSerializer.Serialize(before, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var afterJson = JsonSerializer.Serialize(after, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var beforeFile = Path.Combine(_tempDir, "before.json");
        var afterFile = Path.Combine(_tempDir, "after.json");
        File.WriteAllText(beforeFile, beforeJson);
        File.WriteAllText(afterFile, afterJson);

        var (exitCode, _, stderr) = Run("diff", beforeFile, afterFile);
        Assert.Equal(0, exitCode);
        Assert.Contains("analysis levels differ", stderr);
    }

    [Fact]
    public void DiffSubcommand_SameLevels_NoLevelWarning()
    {
        var result = new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], null, "SyntaxOnly");
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var beforeFile = Path.Combine(_tempDir, "before.json");
        var afterFile = Path.Combine(_tempDir, "after.json");
        File.WriteAllText(beforeFile, json);
        File.WriteAllText(afterFile, json);

        var (exitCode, _, stderr) = Run("diff", beforeFile, afterFile);
        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("analysis levels differ", stderr);
    }

    [Fact]
    public void DiffSubcommand_Help_ExitsZero()
    {
        var (exitCode, stdout, _) = Run("diff", "--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("diff", stdout);
        Assert.Contains("--base-ref", stdout);
    }

    [Fact]
    public void Diff_BaseRef_HappyPath_MatchesManualBeforeSnapshot()
    {
        WriteSimpleProject();
        RunGit(_tempDir, "init");
        GitCommitAll("base");

        var beforeFile = Path.Combine(_tempDir, "before.json");
        Assert.Equal(0, Run("-p", _tempDir, "-f", "json", "-o", beforeFile).ExitCode);

        var worseClass = Path.Combine(_tempDir, "Worse.cs");
        File.WriteAllText(worseClass, """
            namespace Sample;
            public class Worse
            {
                public void M1() {}
                public void M2() {}
                public void M3() {}
                public void M4() {}
                public void M5() {}
                public void M6() {}
                public void M7() {}
                public void M8() {}
                public void M9() {}
                public void M10() {}
                public void M11() {}
            }
            """);
        GitCommitAll("after");

        var afterFile = Path.Combine(_tempDir, "after.json");
        Assert.Equal(0, Run("-p", _tempDir, "-f", "json", "-o", afterFile).ExitCode);

        var manual = Run("diff", beforeFile, afterFile);
        var baseRef = Run("diff", "--base-ref", "HEAD~1", afterFile);
        Assert.Equal(0, manual.ExitCode);
        Assert.Equal(0, baseRef.ExitCode);
        AssertDiffEquivalent(manual.StdOut, baseRef.StdOut);
    }

    [Fact]
    public void Diff_BaseRef_MarkdownWithFailOnRegression_NoRegression_ExitsZero()
    {
        WriteSimpleProject();
        InitGitRepo();
        var afterFile = Path.Combine(_tempDir, "after.json");
        Assert.Equal(0, Run("-p", _tempDir, "-f", "json", "-o", afterFile).ExitCode);

        var (exitCode, stdout, stderr) = Run(
            "diff", "--base-ref", "HEAD", afterFile, "-f", "markdown", "--fail-on-regression");
        Assert.Equal(0, exitCode);
        Assert.Contains("Avg CH", stdout);
        Assert.DoesNotContain("regression:", stderr);
    }

    [Fact]
    public void Diff_BaseRef_UnknownRef_ExitsOneWithFetchHint()
    {
        WriteSimpleProject();
        InitGitRepo();
        var afterFile = Path.Combine(_tempDir, "after.json");
        Assert.Equal(0, Run("-p", _tempDir, "-f", "json", "-o", afterFile).ExitCode);

        var (exitCode, _, stderr) = Run("diff", "--base-ref", "does-not-exist-ref", afterFile);
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown git ref", stderr);
        Assert.Contains("git fetch", stderr);
    }

    [Fact]
    public void Diff_BaseRef_NotGitRepo_ExitsOne()
    {
        WriteSimpleProject();
        var afterFile = Path.Combine(_tempDir, "after.json");
        Assert.Equal(0, Run("-p", _tempDir, "-f", "json", "-o", afterFile).ExitCode);

        var (exitCode, _, stderr) = Run("diff", "--base-ref", "HEAD", afterFile);
        Assert.Equal(1, exitCode);
        Assert.Contains("Not a git repository", stderr);
    }

    [Fact]
    public void Diff_BaseRef_MissingPositional_ExitsOne()
    {
        var (exitCode, _, stderr) = Run("diff", "--base-ref", "HEAD");
        Assert.Equal(1, exitCode);
        Assert.Contains("Usage:", stderr);
    }

    [Fact]
    public void Diff_BaseRef_WithoutValue_ExitsOne()
    {
        var (exitCode, _, stderr) = Run("diff", "--base-ref");
        Assert.Equal(1, exitCode);
        Assert.Contains("--base-ref requires a value", stderr);
    }

    [Fact]
    public void Diff_BaseRef_SubdirectoryProject_ResolvesInsideWorktree()
    {
        var projectDir = Path.Combine(_tempDir, "src", "app");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "Sample.cs"), """
            namespace Sample;
            public class Nested { public string Hi() => "hi"; }
            """);
        InitGitRepo();
        var afterFile = Path.Combine(_tempDir, "after.json");
        Assert.Equal(0, Run("-p", projectDir, "-f", "json", "-o", afterFile).ExitCode);

        var (exitCode, stdout, _) = Run("diff", "--base-ref", "HEAD", afterFile);
        Assert.Equal(0, exitCode);
        Assert.Contains("summary", stdout);
    }

    [Fact]
    public void Diff_BaseRef_CleansUpWorktreeOnSuccessAndFailure()
    {
        WriteSimpleProject();
        InitGitRepo();
        var afterFile = Path.Combine(_tempDir, "after.json");
        Assert.Equal(0, Run("-p", _tempDir, "-f", "json", "-o", afterFile).ExitCode);

        Assert.Equal(0, Run("diff", "--base-ref", "HEAD", afterFile).ExitCode);
        Assert.DoesNotContain("unilyze-worktree", ListGitWorktrees());

        Assert.Equal(1, Run("diff", "--base-ref", "missing-ref", afterFile).ExitCode);
        Assert.DoesNotContain("unilyze-worktree", ListGitWorktrees());
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
    public void Diff_ChangedOnly_OmitsUnchangedBucket()
    {
        var unchangedMetrics = new TypeMetrics(
            "StableClass", "TestNs", "TestAssembly",
            50, 2, 1, 2.0, 3, 2.0, 3, 0, 9.0, [], CodeSmells: []);
        var degradedBefore = new TypeMetrics(
            "BadClass", "TestNs", "TestAssembly",
            100, 5, 2, 3.0, 5, 3.0, 5, 0, 9.0, [], CodeSmells: []);
        var degradedAfter = degradedBefore with { CodeHealth = 6.0 };

        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var beforeFile = Path.Combine(_tempDir, "before.json");
        var afterFile = Path.Combine(_tempDir, "after.json");
        File.WriteAllText(beforeFile, JsonSerializer.Serialize(
            new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], [unchangedMetrics, degradedBefore]), jsonOpts));
        File.WriteAllText(afterFile, JsonSerializer.Serialize(
            new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], [unchangedMetrics, degradedAfter]), jsonOpts));

        var (exitCode, stdout, stderr) = Run("diff", beforeFile, afterFile, "--changed-only");
        Assert.Equal(0, exitCode);
        var doc = JsonDocument.Parse(stdout);
        Assert.Equal(0, doc.RootElement.GetProperty("unchanged").GetArrayLength());
        Assert.True(doc.RootElement.GetProperty("summary").GetProperty("unchangedCount").GetInt32() > 0);
        Assert.True(doc.RootElement.GetProperty("degraded").GetArrayLength() > 0);
        Assert.Contains("Unchanged: 1", stderr);
    }

    [Fact]
    public void Diff_SameSnapshot_ChangedOnly_ZeroAddedRemoved()
    {
        var metrics = new TypeMetrics(
            "StableClass", "TestNs", "TestAssembly",
            50, 2, 1, 2.0, 3, 2.0, 3, 0, 9.0, [], CodeSmells: []);
        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var snapshotFile = Path.Combine(_tempDir, "snapshot.json");
        File.WriteAllText(snapshotFile, JsonSerializer.Serialize(
            new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], [metrics]), jsonOpts));

        var (exitCode, stdout, _) = Run("diff", snapshotFile, snapshotFile, "--changed-only");
        Assert.Equal(0, exitCode);
        var doc = JsonDocument.Parse(stdout);
        Assert.Equal(0, doc.RootElement.GetProperty("added").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("removed").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("summary").GetProperty("addedCount").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("summary").GetProperty("removedCount").GetInt32());
    }

    [Fact]
    public void DiffSubcommand_MarkdownFormat_OutputsGfmTables()
    {
        var warning = new CodeSmell(CodeSmellKind.GodClass, SmellSeverity.Warning, "BadClass", null, "large");
        var beforeMetrics = new TypeMetrics(
            "BadClass", "TestNs", "TestAssembly",
            100, 5, 2, 3.0, 5, 3.0, 5, 0, 9.0, [],
            CodeSmells: []);
        var afterMetrics = new TypeMetrics(
            "BadClass", "TestNs", "TestAssembly",
            100, 5, 2, 10.0, 15, 3.0, 5, 0, 6.0, [],
            CodeSmells: [warning]);

        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var beforeFile = Path.Combine(_tempDir, "before.json");
        var afterFile = Path.Combine(_tempDir, "after.json");
        File.WriteAllText(beforeFile, JsonSerializer.Serialize(
            new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], [beforeMetrics]), jsonOpts));
        File.WriteAllText(afterFile, JsonSerializer.Serialize(
            new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], [afterMetrics]), jsonOpts));

        var (exitCode, stdout, _) = Run("diff", beforeFile, afterFile, "-f", "markdown");
        Assert.Equal(0, exitCode);
        Assert.Contains("Avg CH", stdout);
        Assert.Contains("Warnings", stdout);
        Assert.Contains("| Degraded | 1 |", stdout);
        Assert.Contains("| deltaScore |", stdout);
        Assert.Matches(@"\|\s*-{3,}", stdout);
    }

    [Fact]
    public void Diff_FailOnDeltaBelow_WhenScoreMissesThreshold_ExitsTwo()
    {
        var beforeMetrics = new TypeMetrics(
            "RiskyClass", "TestNs", "TestAssembly",
            100, 1, 1, 2.0, 2, 2.0, 2, 0, 9.0,
            [new MethodMetrics("Run", 2, 2, 1, 0, 10)],
            CodeSmells: []);
        var afterMetrics = beforeMetrics with
        {
            MaxNestingDepth = 4,
            MaxCognitiveComplexity = 15,
            Methods = [new MethodMetrics("Run", 15, 8, 4, 0, 80)],
        };
        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var beforeFile = Path.Combine(_tempDir, "before.json");
        var afterFile = Path.Combine(_tempDir, "after.json");
        File.WriteAllText(beforeFile, JsonSerializer.Serialize(
            new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], [beforeMetrics]), jsonOpts));
        File.WriteAllText(afterFile, JsonSerializer.Serialize(
            new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], [afterMetrics]), jsonOpts));

        var (exitCode, stdout, stderr) = Run(
            "diff", beforeFile, afterFile, "-f", "markdown", "--fail-on-delta-below", "0.5");

        Assert.Equal(2, exitCode);
        Assert.Contains("**Verdict:** FAIL", stdout);
        Assert.Contains("| deltaScore | 0 |", stdout);
        Assert.Contains("deltaScore gate failed", stderr);
    }

    [Fact]
    public void Diff_FailOnDeltaBelow_WithInvalidThreshold_ExitsOne()
    {
        var (exitCode, _, stderr) = Run(
            "diff", "before.json", "after.json", "--fail-on-delta-below", "1.1");

        Assert.Equal(1, exitCode);
        Assert.Contains("number from 0 to 1", stderr);
    }

    [Fact]
    public void Diff_FailOnDeltaBelow_WithNaN_ExitsOne()
    {
        var (exitCode, _, stderr) = Run(
            "diff", "before.json", "after.json", "--fail-on-delta-below", "NaN");

        Assert.Equal(1, exitCode);
        Assert.Contains("number from 0 to 1", stderr);
    }

    [Fact]
    public void Diff_FailOnDeltaBelow_WithoutValue_ExitsOne()
    {
        var (exitCode, _, stderr) = Run(
            "diff", "before.json", "after.json", "--fail-on-delta-below");

        Assert.Equal(1, exitCode);
        Assert.Contains("requires a value", stderr);
    }

    [Fact]
    public void DiffSubcommand_MarkdownFormat_FailOnRegression_ExitsTwoAndPrintsMarkdown()
    {
        var beforeMetrics = new TypeMetrics(
            "BadClass", "TestNs", "TestAssembly",
            100, 5, 2, 3.0, 5, 3.0, 5, 0, 9.0, [], CodeSmells: []);
        var afterMetrics = beforeMetrics with { CodeHealth = 6.0 };

        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var beforeFile = Path.Combine(_tempDir, "before.json");
        var afterFile = Path.Combine(_tempDir, "after.json");
        File.WriteAllText(beforeFile, JsonSerializer.Serialize(
            new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], [beforeMetrics]), jsonOpts));
        File.WriteAllText(afterFile, JsonSerializer.Serialize(
            new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], [], [afterMetrics]), jsonOpts));

        var (exitCode, stdout, stderr) = Run("diff", beforeFile, afterFile, "-f", "markdown", "--fail-on-regression");
        Assert.Equal(2, exitCode);
        Assert.Contains("**Verdict:** FAIL", stdout);
        Assert.Contains("Avg CH", stdout);
        Assert.Contains("regression:", stderr);
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
        Assert.DoesNotContain("MI:", stdout);
    }

    [Fact]
    public void Statusline_ShowMi_OutputsMaintainabilityIndex()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("statusline", "-p", _tempDir, "--show-mi");
        Assert.Equal(0, exitCode);
        Assert.Contains("MI:", stdout);
    }

    [Fact]
    public void Statusline_BackgroundRefresh_ShowMiAndDefaultUseSeparateCaches()
    {
        WriteSimpleProject();
        var defaultCachePath = ResolveStatuslineCachePath(_tempDir);
        var showMiCachePath = ResolveStatuslineCachePath(_tempDir, showMi: true);
        TryDeleteStatuslineCache(defaultCachePath);
        TryDeleteStatuslineCache(showMiCachePath);

        var (defaultColdExit, defaultColdOutput, _) = Run(
            "statusline", "-p", _tempDir, "--background-refresh", "--refresh", "3600");
        var (showMiColdExit, showMiColdOutput, _) = Run(
            "statusline", "-p", _tempDir, "--background-refresh", "--refresh", "3600", "--show-mi");

        Assert.Equal(0, defaultColdExit);
        Assert.Equal(0, showMiColdExit);
        Assert.Empty(defaultColdOutput);
        Assert.Empty(showMiColdOutput);

        WaitForStatuslineCache(defaultCachePath, output => output.Contains("CH:") && !output.Contains("MI:"));
        WaitForStatuslineCache(showMiCachePath, output => output.Contains("MI:"));

        var (defaultWarmExit, defaultWarmOutput, _) = Run(
            "statusline", "-p", _tempDir, "--background-refresh", "--refresh", "3600");
        var (showMiWarmExit, showMiWarmOutput, _) = Run(
            "statusline", "-p", _tempDir, "--background-refresh", "--refresh", "3600", "--show-mi");
        var (_, defaultAgainOutput, _) = Run(
            "statusline", "-p", _tempDir, "--background-refresh", "--refresh", "3600");

        Assert.Equal(0, defaultWarmExit);
        Assert.Equal(0, showMiWarmExit);
        Assert.DoesNotContain("MI:", defaultWarmOutput);
        Assert.Contains("MI:", showMiWarmOutput);
        Assert.DoesNotContain("MI:", defaultAgainOutput);
    }

    [Fact]
    public void Badge_Help_ExitsZero()
    {
        var (exitCode, stdout, _) = Run("badge", "--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("badge", stdout);
        Assert.Contains("--metric", stdout);
    }

    [Fact]
    public void Badge_DefaultMetric_OutputsValidShieldsJson()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("badge", "-p", _tempDir);
        Assert.Equal(0, exitCode);

        var doc = JsonDocument.Parse(stdout);
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("code health", doc.RootElement.GetProperty("label").GetString());
    }

    [Fact]
    public void Badge_MiMetric_OutputsMaintainability()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("badge", "-p", _tempDir, "--metric", "mi");
        Assert.Equal(0, exitCode);
        Assert.Equal("maintainability", JsonDocument.Parse(stdout).RootElement.GetProperty("label").GetString());
    }

    [Fact]
    public void Badge_SmellsMetric_OutputsSmells()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("badge", "-p", _tempDir, "--metric", "smells");
        Assert.Equal(0, exitCode);

        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal("smells", root.GetProperty("label").GetString());
        Assert.True(int.TryParse(root.GetProperty("message").GetString(), out _));
    }

    [Fact]
    public void Badge_UnknownMetric_ExitsNonZero()
    {
        var (exitCode, _, stderr) = Run("badge", "-p", _tempDir, "--metric", "foo");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown metric", stderr);
    }

    [Fact]
    public void Badge_FormatSvg_OutputsSvg()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("badge", "-p", _tempDir, "--format", "svg");
        Assert.Equal(0, exitCode);
        Assert.StartsWith("<svg", stdout.TrimStart());
        XDocument.Parse(stdout);
    }

    [Fact]
    public void Badge_UnknownFormat_ExitsNonZero()
    {
        var (exitCode, _, stderr) = Run("badge", "-p", _tempDir, "--format", "bogus");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown format", stderr);
    }

    [Fact]
    public void Badge_OutputFlag_WritesFile()
    {
        WriteSimpleProject();
        var outFile = Path.Combine(_tempDir, "badge.json");
        var (exitCode, _, _) = Run("badge", "-p", _tempDir, "-o", outFile);
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outFile));
        Assert.Equal(1, JsonDocument.Parse(File.ReadAllText(outFile)).RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void Badge_EmptyProject_NaLightgrey()
    {
        var (exitCode, stdout, _) = Run("badge", "-p", _tempDir);
        Assert.Equal(0, exitCode);

        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal("n/a", root.GetProperty("message").GetString());
        Assert.Equal("lightgrey", root.GetProperty("color").GetString());
    }

    [Fact]
    public void Badge_GateOnEmptyProject_FailsClosed()
    {
        // No types analyzed: the gate must fail (exit 2) rather than report a false green.
        var (exitCode, _, stderr) = Run("badge", "-p", _tempDir, "--metric", "codehealth", "--fail-under", "7");
        Assert.Equal(2, exitCode);
        Assert.Contains("metric unavailable", stderr);
    }

    [Fact]
    public void Badge_GatePass_ExitsZero()
    {
        WriteSimpleProject();
        var (exitCode, _, _) = Run("badge", "-p", _tempDir, "--metric", "codehealth", "--fail-under", "1");
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Badge_FailUnderWithoutValue_ExitsUsageError()
    {
        // Trailing value-less gate flag must be a usage error, not a silently skipped gate.
        var (exitCode, _, stderr) = Run("badge", "-p", _tempDir, "--metric", "codehealth", "--fail-under");
        Assert.Equal(1, exitCode);
        Assert.Contains("--fail-under requires a value", stderr);
    }

    [Fact]
    public void Badge_FailOverWithoutValue_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("badge", "-p", _tempDir, "--metric", "smells", "--fail-over");
        Assert.Equal(1, exitCode);
        Assert.Contains("--fail-over requires a value", stderr);
    }

    [Fact]
    public void Projects_Analyze_WritesPerProjectFilesAndSummary()
    {
        var monoRoot = WriteMonorepoFixture();
        var outDir = Path.Combine(_tempDir, "snapshots");
        var glob = Path.Combine(monoRoot, "packages", "*");

        var (exitCode, _, stderr) = Run("--projects", glob, "-o", outDir, "-f", "json");
        Assert.Equal(0, exitCode);

        Assert.True(File.Exists(Path.Combine(outDir, "a.json")));
        Assert.True(File.Exists(Path.Combine(outDir, "b.json")));
        Assert.True(File.Exists(Path.Combine(outDir, "summary.json")));
        Assert.Contains("Written to", stderr);

        var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "summary.json"))).RootElement;
        Assert.Equal(2, summary.GetProperty("projects").GetArrayLength());
        Assert.True(summary.GetProperty("toolVersion").GetString()?.StartsWith("0.", StringComparison.Ordinal) == true);

        var aDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "a.json"))).RootElement;
        Assert.Equal(JsonValueKind.String, aDoc.GetProperty("projectPath").ValueKind);
    }

    [Fact]
    public void Projects_Badge_AggregatesGateExitCode()
    {
        var monoRoot = WriteMonorepoFixture();
        var outDir = Path.Combine(_tempDir, "badges");
        var glob = Path.Combine(monoRoot, "packages", "*");

        var (exitCode, _, stderr) = Run(
            "badge", "--projects", glob, "--metric", "codehealth", "--fail-under", "7", "-o", outDir);
        Assert.Equal(2, exitCode);

        Assert.True(File.Exists(Path.Combine(outDir, "a-codehealth.json")));
        Assert.True(File.Exists(Path.Combine(outDir, "b-codehealth.json")));
        Assert.True(File.Exists(Path.Combine(outDir, "summary.json")));
        Assert.Contains("| Project | Value | Gate |", stderr);

        var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "summary.json"))).RootElement;
        var gates = summary.GetProperty("projects").EnumerateArray()
            .Select(p => p.GetProperty("gate").GetString())
            .ToList();
        Assert.Contains("pass", gates);
        Assert.Contains("fail", gates);
    }

    [Fact]
    public void Projects_Badge_AllPass_ExitsZero()
    {
        var monoRoot = WriteMonorepoFixture();
        var outDir = Path.Combine(_tempDir, "badges-pass");
        var glob = Path.Combine(monoRoot, "packages", "*");

        var (exitCode, _, stderr) = Run(
            "badge", "--projects", glob, "--metric", "codehealth", "--fail-under", "1", "-o", outDir);
        Assert.Equal(0, exitCode);
        Assert.Contains("| Project | Value | Gate |", stderr);
    }

    [Fact]
    public void Projects_CombinedWithPath_ExitsUsageError()
    {
        var monoRoot = WriteMonorepoFixture();
        var glob = Path.Combine(monoRoot, "packages", "*");
        var (exitCode, _, stderr) = Run("-p", _tempDir, "--projects", glob, "-o", _tempDir);
        Assert.Equal(1, exitCode);
        Assert.Contains("cannot be combined with -p", stderr);
    }

    [Fact]
    public void Projects_ZeroMatches_ExitsUsageError()
    {
        var glob = Path.Combine(_tempDir, "nomatch", "*");
        var (exitCode, _, stderr) = Run("badge", "--projects", glob, "--metric", "codehealth", "--fail-under", "7", "-o", _tempDir);
        Assert.Equal(1, exitCode);
        Assert.Contains("No directories matched", stderr);
    }

    [Fact]
    public void Projects_MissingOutputDir_ExitsUsageError()
    {
        var monoRoot = WriteMonorepoFixture();
        var glob = Path.Combine(monoRoot, "packages", "*");
        var (exitCode, _, stderr) = Run("badge", "--projects", glob, "--metric", "codehealth", "--fail-under", "7");
        Assert.Equal(1, exitCode);
        Assert.Contains("requires -o <dir>", stderr);
    }

    [Fact]
    public void Projects_HtmlFormat_ExitsUsageError()
    {
        var monoRoot = WriteMonorepoFixture();
        var glob = Path.Combine(monoRoot, "packages", "*");
        var (exitCode, _, stderr) = Run("--projects", glob, "-o", _tempDir, "-f", "html");
        Assert.Equal(1, exitCode);
        Assert.Contains("HTML output is not supported with --projects", stderr);
    }

    [Fact]
    public void Projects_UnknownOnDiff_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("diff", "a.json", "b.json", "--projects", "x/*");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option: '--projects'", stderr);
    }

    [Fact]
    public void Analyze_UnknownOption_ExitsUsageError()
    {
        WriteSimpleProject();
        var (exitCode, _, stderr) = Run("-p", _tempDir, "--bogus");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option: '--bogus'", stderr);
    }

    [Fact]
    public void Analyze_UnknownSubcommand_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("nonexistent");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown subcommand: 'nonexistent'", stderr);
    }

    [Fact]
    public void Config_UnknownSubcommand_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("config", "nonexistent");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown subcommand: 'nonexistent'", stderr);
    }

    [Fact]
    public void Config_UnknownOption_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("config", "list", "--bogus");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option: '--bogus'", stderr);
    }

    [Fact]
    public void Config_List_HappyPath_ExitsZero()
    {
        var (exitCode, stdout, _) = Run("config", "list");
        Assert.Equal(0, exitCode);
        Assert.Contains("No configuration found.", stdout);
    }

    [Fact]
    public void Skills_UnknownSubcommand_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("skills", "nonexistent");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown subcommand: 'nonexistent'", stderr);
    }

    [Fact]
    public void Skills_UnknownOption_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("skills", "list", "--bogus");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option: '--bogus'", stderr);
    }

    [Fact]
    public void Skills_List_HappyPath_ExitsZero()
    {
        var (exitCode, _, stderr) = Run("skills", "list");
        Assert.Equal(0, exitCode);
        Assert.Contains("unilyze Skills Status:", stderr);
    }

    [Fact]
    public void Diff_UnknownSubcommand_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("dif");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown subcommand: 'dif'", stderr);
        Assert.Contains("Did you mean 'diff'?", stderr);
    }

    [Fact]
    public void Diff_UnknownOption_ExitsUsageError()
    {
        var beforeFile = Path.Combine(_tempDir, "before.json");
        var afterFile = Path.Combine(_tempDir, "after.json");
        File.WriteAllText(beforeFile, "{}");
        File.WriteAllText(afterFile, "{}");

        var (exitCode, _, stderr) = Run("diff", beforeFile, afterFile, "--bogus");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option: '--bogus'", stderr);
    }

    [Fact]
    public void Hotspot_UnknownSubcommand_ExitsUsageError()
    {
        WriteSimpleProject();
        var (exitCode, _, stderr) = Run("hotspot", "bogus", "-p", _tempDir);
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown subcommand: 'bogus'", stderr);
    }

    [Fact]
    public void Hotspot_UnknownOption_ExitsUsageError()
    {
        WriteSimpleProject();
        var (exitCode, _, stderr) = Run("hotspot", "-p", _tempDir, "--bogus");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option: '--bogus'", stderr);
    }

    [Fact]
    public void Hotspot_HappyPath_ExitsZero()
    {
        WriteSimpleProject();
        InitGitRepo();
        var (exitCode, _, stderr) = Run("hotspot", "-p", _tempDir);
        Assert.Equal(0, exitCode);
        Assert.Contains("Hotspot analysis:", stderr);
    }

    [Fact]
    public void Hotspot_BotFilter_DefaultExcludesDependabotCommits()
    {
        WriteSimpleProject();
        InitGitRepo();
        File.AppendAllText(Path.Combine(_tempDir, "Sample.cs"), "\n// bot change\n");
        GitCommitAs("dependabot[bot]", "dependabot@users.noreply.github.com", "bot bump");

        var (filteredExit, filteredStdout, filteredStderr) = Run("hotspot", "-p", _tempDir);
        Assert.Equal(0, filteredExit);
        Assert.Contains("Bot commits excluded:", filteredStderr);
        var filtered = JsonDocument.Parse(filteredStdout).RootElement;
        Assert.True(filtered.GetProperty("botFilter").GetBoolean());
        Assert.Equal(1, filtered.GetProperty("botCommitsExcluded").GetInt32());

        var (rawExit, rawStdout, _) = Run("hotspot", "-p", _tempDir, "--no-bot-filter");
        Assert.Equal(0, rawExit);
        var raw = JsonDocument.Parse(rawStdout).RootElement;
        Assert.False(raw.GetProperty("botFilter").GetBoolean());
        Assert.Equal(0, raw.GetProperty("botCommitsExcluded").GetInt32());
        Assert.True(
            raw.GetProperty("hotspots")[0].GetProperty("changeCount").GetInt32()
            >= filtered.GetProperty("hotspots")[0].GetProperty("changeCount").GetInt32());
    }

    [Fact]
    public void Hotspot_InvalidBotPattern_ExitsOne()
    {
        WriteSimpleProject();
        InitGitRepo();
        var (exitCode, _, stderr) = Run("hotspot", "-p", _tempDir, "--bot-pattern", "[");
        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid bot pattern", stderr);
    }

    [Fact]
    public void Hotspot_HalfLife_DeterministicAcrossRuns()
    {
        WriteSimpleProject();
        InitGitRepo();
        File.AppendAllText(Path.Combine(_tempDir, "Sample.cs"), "\n// change 1\n");
        GitCommitAll("change 1");
        File.AppendAllText(Path.Combine(_tempDir, "Sample.cs"), "\n// change 2\n");
        GitCommitAll("change 2");

        var (exit1, stdout1, _) = Run("hotspot", "-p", _tempDir, "--half-life", "90.day");
        var (exit2, stdout2, _) = Run("hotspot", "-p", _tempDir, "--half-life", "90.day");
        Assert.Equal(0, exit1);
        Assert.Equal(0, exit2);
        Assert.Equal(stdout1, stdout2);
    }

    [Fact]
    public void Hotspot_MethodsMode_EmitsMethodHotspots()
    {
        WriteSimpleProject();
        InitGitRepo();
        File.AppendAllText(Path.Combine(_tempDir, "Sample.cs"), "\n// method churn\n");
        GitCommitAll("method churn");

        var (exitCode, stdout, stderr) = Run("hotspot", "-p", _tempDir, "--methods", "Sample.cs");
        Assert.Equal(0, exitCode);
        Assert.Contains("Method hotspots:", stderr);
        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal(JsonValueKind.Array, root.GetProperty("methodHotspots").ValueKind);
        Assert.True(root.GetProperty("methodHotspots").GetArrayLength() > 0);
    }

    [Fact]
    public void Hotspot_Help_MentionsNewFlags()
    {
        var (exitCode, stdout, _) = Run("hotspot", "--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("--half-life", stdout);
        Assert.Contains("--no-bot-filter", stdout);
        Assert.Contains("--methods", stdout);
    }

    [Fact]
    public void Trend_UnknownSubcommand_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("trendd");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown subcommand: 'trendd'", stderr);
        Assert.Contains("Did you mean 'trend'?", stderr);
    }

    [Fact]
    public void Trend_UnknownOption_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("trend", _tempDir, "--bogus");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option: '--bogus'", stderr);
    }

    [Fact]
    public void Trend_HappyPath_ExitsZero()
    {
        var snapshot = JsonSerializer.Serialize(
            new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], []),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(Path.Combine(_tempDir, "snapshot.json"), snapshot);

        var (exitCode, stdout, _) = Run("trend", _tempDir);
        Assert.Equal(0, exitCode);
        Assert.Contains("snapshotCount", stdout);
    }

    [Fact]
    public void Trend_HtmlOutput_WritesSelfContainedFile()
    {
        var snapshot = JsonSerializer.Serialize(
            new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], []),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(Path.Combine(_tempDir, "snapshot.json"), snapshot);

        var htmlPath = Path.Combine(_tempDir, "trend.html");
        var (exitCode, _, stderr) = Run("trend", _tempDir, "-o", htmlPath, "--no-open");
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(htmlPath));
        var html = File.ReadAllText(htmlPath);
        Assert.Contains("<svg", html);
        Assert.DoesNotContain("<script src=", html);
        Assert.DoesNotContain("unpkg.com", html);
        Assert.Contains("Written to", stderr);
    }

    [Fact]
    public void Trend_UnsupportedFormat_ExitsUsageError()
    {
        var snapshot = JsonSerializer.Serialize(
            new AnalysisResult("/test", DateTimeOffset.UtcNow, [], [], []),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(Path.Combine(_tempDir, "snapshot.json"), snapshot);

        var (exitCode, _, stderr) = Run("trend", _tempDir, "-f", "sarif");
        Assert.Equal(1, exitCode);
        Assert.Contains("Trend does not support SARIF", stderr);
    }

    [Fact]
    public void Calibrate_Help_ExitsZero()
    {
        var (exitCode, stdout, _) = Run("calibrate", "--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("unilyze calibrate", stdout);
        Assert.Contains("dir-of-jsons", stdout);
    }

    [Fact]
    public void Calibrate_UnknownSubcommand_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("calibrated");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown subcommand: 'calibrated'", stderr);
        Assert.Contains("Did you mean 'calibrate'?", stderr);
    }

    [Fact]
    public void Calibrate_UnknownOption_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("calibrate", _tempDir, "--bogus");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option: '--bogus'", stderr);
    }

    [Fact]
    public void Calibrate_HappyPath_ExitsZero()
    {
        WriteCalibrateSnapshot("a.json", "system-a", 10, 4, 2);
        WriteCalibrateSnapshot("b.json", "system-b", 20, 8, 3);

        var (exitCode, stdout, stderr) = Run("calibrate", _tempDir);
        Assert.Equal(0, exitCode);
        Assert.Contains("methodology", stdout);
        Assert.Contains("riskCategories", stdout);
        Assert.Contains("unilyzeConfigFragment", stdout);
        Assert.Contains("Calibration:", stderr);
        Assert.Contains("LongMethod (LOC)", stderr);
    }

    [Fact]
    public void Calibrate_MetricsVersionMismatch_ExitsUsageError()
    {
        WriteCalibrateSnapshot("current.json", "current", 10, 4, 2);
        WriteCalibrateSnapshot("older.json", "older", 20, 8, 3, metricsVersion: 2);

        var (exitCode, _, stderr) = Run("calibrate", _tempDir);
        Assert.Equal(1, exitCode);
        Assert.Contains("metricsVersion mismatch", stderr);
    }

    [Fact]
    public void Calibrate_TooFewSnapshots_ExitsUsageError()
    {
        WriteCalibrateSnapshot("only.json", "only", 10, 4, 2);

        var (exitCode, _, stderr) = Run("calibrate", _tempDir);
        Assert.Equal(1, exitCode);
        Assert.Contains("At least two JSON snapshots are required", stderr);
    }

    void WriteCalibrateSnapshot(
        string fileName,
        string projectPath,
        int methodLoc,
        int cycCc,
        int cogCc,
        int metricsVersion = AnalysisResult.CurrentMetricsVersion)
    {
        var typeMetrics = new[]
        {
            new TypeMetrics(
                "SampleType",
                "Sample",
                "SampleAsm",
                methodLoc * 2,
                1,
                1,
                cogCc,
                cogCc,
                cycCc,
                cycCc,
                0,
                8.0,
                [
                    new MethodMetrics("Run", cogCc, cycCc, 1, 2, methodLoc),
                ]),
        };

        var snapshot = new AnalysisResult(
            projectPath,
            DateTimeOffset.UtcNow,
            [],
            [],
            [],
            typeMetrics,
            MetricsVersion: metricsVersion,
            ToolVersion: "test");

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        File.WriteAllText(Path.Combine(_tempDir, fileName), json);
    }

    [Fact]
    public void Statusline_UnknownSubcommand_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("statuslin", "-p", ".");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown subcommand: 'statuslin'", stderr);
        Assert.Contains("Did you mean 'statusline'?", stderr);
    }

    [Fact]
    public void Statusline_UnknownOption_ExitsUsageError()
    {
        WriteSimpleProject();
        var (exitCode, _, stderr) = Run("statusline", "-p", _tempDir, "--bogus");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option: '--bogus'", stderr);
    }

    [Fact]
    public void Badge_UnknownSubcommand_ExitsUsageError()
    {
        WriteSimpleProject();
        var (exitCode, _, stderr) = Run("badge", "bogus", "-p", _tempDir);
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown subcommand: 'bogus'", stderr);
    }

    [Fact]
    public void Metrics_UnknownSubcommand_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("metrics", "bogus");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown subcommand: 'bogus'", stderr);
    }

    [Fact]
    public void Metrics_UnknownOption_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("metrics", "--bogus");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option: '--bogus'", stderr);
    }

    [Fact]
    public void Metrics_HappyPath_ExitsZero()
    {
        var (exitCode, stdout, _) = Run("metrics");
        Assert.Equal(0, exitCode);
        Assert.Contains("unilyze metrics", stdout);
    }

    [Fact]
    public void Schema_UnknownSubcommand_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("schema", "bogus");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown subcommand: 'bogus'", stderr);
    }

    [Fact]
    public void Schema_UnknownOption_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("schema", "--bogus");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option: '--bogus'", stderr);
    }

    [Fact]
    public void Schema_HappyPath_ExitsZero()
    {
        var (exitCode, stdout, _) = Run("schema");
        Assert.Equal(0, exitCode);
        Assert.Contains("unilyze schema", stdout);
    }

    private static void AssertDiffEquivalent(string manualJson, string baseRefJson)
    {
        var manual = JsonDocument.Parse(manualJson).RootElement;
        var baseRef = JsonDocument.Parse(baseRefJson).RootElement;

        Assert.Equal(
            manual.GetProperty("summary").GetRawText(),
            baseRef.GetProperty("summary").GetRawText());

        foreach (var bucket in new[] { "improved", "degraded", "unchanged", "added", "removed" })
        {
            var manualKeys = manual.GetProperty(bucket).EnumerateArray()
                .Select(e => e.GetProperty("typeKey").GetString())
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();
            var baseRefKeys = baseRef.GetProperty(bucket).EnumerateArray()
                .Select(e => e.GetProperty("typeKey").GetString())
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(manualKeys, baseRefKeys);
        }
    }

    [Fact]
    public void Query_UnknownOption_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("query", "--bogus");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option: '--bogus'", stderr);
    }

    [Fact]
    public void Query_UnknownSubcommand_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("query", "bogus", "-i", "snap.json");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown subcommand: 'bogus'", stderr);
    }

    [Fact]
    public void Query_Help_ListsFlags()
    {
        var (exitCode, stdout, _) = Run("query", "--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("--worst", stdout);
        Assert.Contains("--type", stdout);
        Assert.Contains("-f, --format", stdout);
        Assert.Contains("--include-api-surface", stdout);
    }

    [Fact]
    public void JsonOutput_WithoutApiSurfaceFlag_OmitsApiSurfaceKey()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("-p", _tempDir, "-f", "json");
        Assert.Equal(0, exitCode);
        Assert.False(JsonDocument.Parse(stdout).RootElement.TryGetProperty("apiSurface", out _));
    }

    [Fact]
    public void JsonOutput_WithIncludeApiSurface_EmitsApiSurface()
    {
        WriteDocCommentProject();
        var (exitCode, stdout, _) = Run("-p", _tempDir, "-f", "json", "--include-api-surface");
        Assert.Equal(0, exitCode);

        var root = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal(JsonValueKind.Array, root.GetProperty("apiSurface").ValueKind);
        Assert.True(root.GetProperty("apiSurface").GetArrayLength() > 0);
    }

    [Fact]
    public void IncludeApiSurface_MisspelledFlag_ExitsUsageError()
    {
        var (exitCode, _, stderr) = Run("--include-api-surfaces");
        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option: '--include-api-surfaces'", stderr);
        Assert.Contains("Did you mean '--include-api-surface'?", stderr);
    }

    [Fact]
    public void Query_IncludeApiSurface_FromSnapshotWithoutSurface_ExitsUsageError()
    {
        var fixturePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "golden", "expected.json"));
        var (exitCode, _, stderr) = Run("query", "--worst", "3", "-i", fixturePath, "--include-api-surface");
        Assert.Equal(1, exitCode);
        Assert.Contains("Snapshot lacks apiSurface", stderr);
    }

    [Fact]
    public void Query_IncludeApiSurface_FromProject_IncludesApiSurfaceSection()
    {
        WriteDocCommentProject();
        var (exitCode, stdout, _) = Run("query", "--worst", "1", "-p", _tempDir, "--include-api-surface");
        Assert.Equal(0, exitCode);
        Assert.Contains("### API Surface", stdout);
        Assert.Contains("#### Public signatures", stdout);
        Assert.Contains("#### Identifiers", stdout);
    }

    [Fact]
    public void Help_ListsIncludeApiSurfaceFlag()
    {
        var (exitCode, stdout, _) = Run("--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("--include-api-surface", stdout);
    }

    [Fact]
    public void Query_WorstFromSnapshot_IncludesAnchors()
    {
        var fixturePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "golden", "expected.json"));
        var (exitCode, stdout, stderr) = Run("query", "--worst", "3", "-i", fixturePath);
        Assert.Equal(0, exitCode);
        Assert.Contains("Query evidence pack:", stderr);
        Assert.Contains("CH ", stdout);
        Assert.Contains("### Smells", stdout);
        Assert.Contains("@ `", stdout);
    }

    [Fact]
    public void Query_AmbiguousTypeName_ExitsUsageError()
    {
        var metrics = new[]
        {
            new TypeMetrics("Dup", "Alpha", "Test", 1, 0, 0, 0, 0, 0, 0, 0, 5.0, []),
            new TypeMetrics("Dup", "Beta", "Test", 1, 0, 0, 0, 0, 0, 0, 0, 6.0, []),
        };
        var snapshot = new AnalysisResult("/tmp", DateTimeOffset.UtcNow, [], [], [], metrics);
        var snapshotPath = Path.Combine(_tempDir, "dup-types.json");
        File.WriteAllText(
            snapshotPath,
            JsonSerializer.Serialize(snapshot, AnalysisJsonContext.Default.AnalysisResult));

        var (exitCode, _, stderr) = Run("query", "--type", "Dup", "-i", snapshotPath);
        Assert.Equal(1, exitCode);
        Assert.Contains("Ambiguous type name 'Dup'", stderr);
        Assert.Contains("Alpha.Dup", stderr);
    }

    [Fact]
    public void Help_ListsQuerySubcommand()
    {
        var (exitCode, stdout, _) = Run("--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("unilyze query", stdout);
    }

    private void InitGitRepo()
    {
        RunGit(_tempDir, "init");
        GitCommitAll("init");
    }

    private void GitCommitAll(string message)
    {
        RunGit(_tempDir, "add", ".");
        RunGit(_tempDir, "-c", "user.email=test@test.com", "-c", "user.name=test", "commit", "-m", message);
    }

    private void GitCommitAs(string name, string email, string message)
    {
        RunGit(_tempDir, "add", ".");
        RunGit(_tempDir, "-c", $"user.email={email}", "-c", $"user.name={name}", "commit", "-m", message);
    }

    private static string ListGitWorktrees()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "worktree list",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(30_000))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup before reporting the timeout.
            }

            throw new TimeoutException("git worktree list timed out");
        }

        _ = stderrTask.GetAwaiter().GetResult();
        return stdoutTask.GetAwaiter().GetResult();
    }

    private static void RunGit(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(30_000))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup before reporting the timeout.
            }

            throw new TimeoutException($"git {string.Join(' ', args)} timed out");
        }

        _ = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
    }

    private string WriteMonorepoFixture()
    {
        var monoRoot = Path.Combine(_tempDir, "mono");
        var packageA = Path.Combine(monoRoot, "packages", "a");
        var packageB = Path.Combine(monoRoot, "packages", "b");
        Directory.CreateDirectory(packageA);
        Directory.CreateDirectory(packageB);

        File.WriteAllText(Path.Combine(packageA, "Clean.cs"), """
            namespace PackageA;
            public class Clean
            {
                public string Greet(string name) => $"Hello, {name}!";
            }
            """);

        File.WriteAllText(Path.Combine(packageB, "Degraded.cs"), """
            namespace PackageB;
            public class Degraded
            {
                public int Run(int x)
                {
                    if (x == 1) return 1; if (x == 2) return 2; if (x == 3) return 3; if (x == 4) return 4; if (x == 5) return 5;
                    if (x == 6) return 6; if (x == 7) return 7; if (x == 8) return 8; if (x == 9) return 9; if (x == 10) return 10;
                    if (x == 11) return 11; if (x == 12) return 12; if (x == 13) return 13; if (x == 14) return 14; if (x == 15) return 15;
                    if (x == 16) return 16; if (x == 17) return 17; if (x == 18) return 18; if (x == 19) return 19; if (x == 20) return 20;
                    if (x == 21) return 21; if (x == 22) return 22; if (x == 23) return 23; if (x == 24) return 24; if (x == 25) return 25;
                    if (x == 26) return 26; if (x == 27) return 27; if (x == 28) return 28; if (x == 29) return 29; if (x == 30) return 30;
                    if (x == 31) return 31; if (x == 32) return 32; if (x == 33) return 33; if (x == 34) return 34; if (x == 35) return 35;
                    if (x == 36) return 36; if (x == 37) return 37; if (x == 38) return 38; if (x == 39) return 39; if (x == 40) return 40;
                    if (x == 41) return 41; if (x == 42) return 42; if (x == 43) return 43; if (x == 44) return 44; if (x == 45) return 45;
                    if (x == 46) return 46; if (x == 47) return 47; if (x == 48) return 48; if (x == 49) return 49; if (x == 50) return 50;
                    if (x == 51) return 51; if (x == 52) return 52; if (x == 53) return 53; if (x == 54) return 54; if (x == 55) return 55;
                    if (x == 56) return 56; if (x == 57) return 57; if (x == 58) return 58; if (x == 59) return 59; if (x == 60) return 60;
                    if (x == 61) return 61; if (x == 62) return 62; if (x == 63) return 63; if (x == 64) return 64; if (x == 65) return 65;
                    if (x == 66) return 66; if (x == 67) return 67; if (x == 68) return 68; if (x == 69) return 69; if (x == 70) return 70;
                    if (x == 71) return 71; if (x == 72) return 72; if (x == 73) return 73; if (x == 74) return 74; if (x == 75) return 75;
                    if (x == 76) return 76; if (x == 77) return 77; if (x == 78) return 78; if (x == 79) return 79; if (x == 80) return 80;
                    if (x == 81) return 81; if (x == 82) return 82; if (x == 83) return 83; if (x == 84) return 84; if (x == 85) return 85;
                    if (x == 86) return 86; if (x == 87) return 87; if (x == 88) return 88; if (x == 89) return 89; if (x == 90) return 90;
                    if (x == 91) return 91; if (x == 92) return 92; if (x == 93) return 93; if (x == 94) return 94; if (x == 95) return 95;
                    if (x == 96) return 96; if (x == 97) return 97; if (x == 98) return 98; if (x == 99) return 99;
                    return 0;
                }
            }
            """);

        return monoRoot;
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

    private void WriteDocCommentProject()
    {
        var csFile = Path.Combine(_tempDir, "Sample.cs");
        File.WriteAllText(csFile, """
            namespace Sample;

            /// <summary>Greets callers.</summary>
            public class Greeter
            {
                /// <summary>Says hello.</summary>
                public string Greet(string name) => $"Hello, {name}!";
            }
            """);
    }

    private static string ResolveStatuslineCachePath(
        string projectPath,
        bool useCodeHealthV1 = false,
        bool showMi = false)
    {
        var fullPath = ProgramHelpers.ResolveProjectRoot(projectPath);
        var cacheKey = fullPath;
        if (useCodeHealthV1)
            cacheKey += "\0codehealth-v1";
        if (showMi)
            cacheKey += "\0show-mi";
        var hash = ComputeStatuslineCacheHash(cacheKey);
        return Path.Combine(Path.GetTempPath(), $"unilyze-sl-{hash}.txt");
    }

    private static string ComputeStatuslineCacheHash(string path)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(path));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void TryDeleteStatuslineCache(string cachePath)
    {
        try
        {
            if (File.Exists(cachePath))
                File.Delete(cachePath);
        }
        catch
        {
            // Best-effort cleanup for isolated E2E runs.
        }
    }

    private static void WaitForStatuslineCache(string cachePath, Func<string, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(cachePath) && predicate(File.ReadAllText(cachePath)))
                return;
            Thread.Sleep(250);
        }

        var content = File.Exists(cachePath) ? File.ReadAllText(cachePath) : "<missing>";
        Assert.Fail($"Expected matching statusline cache at {cachePath}. Actual: {content}");
    }

    private void WriteCsprojWithValidReference()
    {
        // A <Reference> with an existing HintPath makes CsprojParser contribute
        // reference paths, which auto-elevates SyntaxOnly to CoreEngine.
        var dllPath = typeof(object).Assembly.Location;
        var csprojFile = Path.Combine(_tempDir, "Sample.csproj");
        File.WriteAllText(csprojFile, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Reference Include="CoreLib">
                  <HintPath>{dllPath}</HintPath>
                </Reference>
              </ItemGroup>
            </Project>
            """);
    }
}
