using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

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
        Assert.Equal("CoreEngine", unpinnedRoot.GetProperty("analysisLevel").GetString());

        var (pinnedExit, pinnedStdout, _) = Run("-p", _tempDir, "-f", "json", "--level", "syntax");
        Assert.Equal(0, pinnedExit);
        var pinnedRoot = JsonDocument.Parse(pinnedStdout).RootElement;
        Assert.Equal("SyntaxOnly", pinnedRoot.GetProperty("analysisLevel").GetString());
    }

    [Fact]
    public void Level_Complete_OnNonUnityProject_ExitsNonZero()
    {
        // A bare directory of .cs files can only resolve to SyntaxOnly, so a Complete pin must fail.
        WriteSimpleProject();
        var (exitCode, _, stderr) = Run("-p", _tempDir, "-f", "json", "--level", "complete");
        Assert.Equal(1, exitCode);
        Assert.Contains("Requested analysis level", stderr);
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
        Assert.Equal("SyntaxOnly", level.GetString());
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
        Assert.Equal("SyntaxOnly", root.GetProperty("analysisLevel").GetString());
    }

    [Fact]
    public void Badge_LevelComplete_OnNonUnityProject_ExitsNonZero()
    {
        WriteSimpleProject();
        var (exitCode, _, stderr) = Run("badge", "-p", _tempDir, "--level", "complete");
        Assert.Equal(1, exitCode);
        Assert.Contains("Requested analysis level", stderr);
    }

    [Fact]
    public void Statusline_Help_MentionsLevel()
    {
        var (exitCode, stdout, _) = Run("statusline", "--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("--level", stdout);
    }

    [Fact]
    public void Statusline_SyntaxOnlyProject_ShowsLevelMarker()
    {
        WriteSimpleProject();
        var (exitCode, stdout, _) = Run("statusline", "-p", _tempDir);
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
        Assert.Matches(@"\|\s*-{3,}", stdout);
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
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(30_000);
        return stdout;
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
        proc.WaitForExit(30_000);
        if (proc.ExitCode != 0)
        {
            var stderr = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
        }
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
