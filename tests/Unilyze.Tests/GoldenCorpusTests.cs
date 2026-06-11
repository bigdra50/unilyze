using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Unilyze.Tests;

public sealed class GoldenCorpusTests
{
    const string GoldenProjectPath = "/golden-fixture";
    const string UpdateEnvVar = "UNILYZE_GOLDEN_UPDATE";

    static readonly string FixtureRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "golden"));

    static readonly string ExpectedPath = Path.Combine(FixtureRoot, "expected.json");
    static readonly string CsprojPath = Path.Combine(FixtureRoot, "Golden.csproj");

    static readonly string CurrentTargetFramework = ResolveCurrentTargetFramework();
    static readonly string DotnetHostPath = ResolveDotnetHostPath();
    static readonly string AppDllPath = ResolveAppDllPath();

    [Fact]
    public void GoldenFixture_MatchesPinnedMetricsJson()
    {
        EnsureCsprojWithCoreEngineReference();

        var (exitCode, stdout, stderr) = Run("-p", FixtureRoot, "-f", "json");
        Assert.Equal(0, exitCode);

        var actual = NormalizeForComparison(stdout);
        var root = JsonNode.Parse(actual)?.AsObject()
            ?? throw new InvalidOperationException("Failed to parse normalized golden JSON.");
        Assert.Equal("CoreEngine", root["analysisLevel"]?.GetValue<string>());
        Assert.Equal("unity", root["projectKind"]?.GetValue<string>());

        if (IsUpdateRequested())
        {
            File.WriteAllText(ExpectedPath, actual);
            return;
        }

        Assert.True(File.Exists(ExpectedPath),
            $"Missing {ExpectedPath}. Regenerate with: UNILYZE_GOLDEN_UPDATE=1 dotnet test tests/Unilyze.Tests -f {CurrentTargetFramework} --filter GoldenCorpus");

        var expected = File.ReadAllText(ExpectedPath);
        Assert.Equal(expected, actual);
    }

    static void EnsureCsprojWithCoreEngineReference()
    {
        // Unity golden still needs a resolvable reference when the editor is absent in CI.
        var dllPath = typeof(object).Assembly.Location;
        File.WriteAllText(CsprojPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Reference Include="CoreLib">
                  <HintPath>{dllPath}</HintPath>
                </Reference>
              </ItemGroup>
            </Project>
            """);
    }

    static string NormalizeForComparison(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Golden analysis output was not valid JSON.");

        var fixtureRoot = Path.GetFullPath(FixtureRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var assetsDir = Path.Combine(fixtureRoot, "Assets")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        root["projectPath"] = GoldenProjectPath;
        root.AsObject().Remove("analyzedAt");
        root.AsObject().Remove("toolVersion");

        NormalizePaths(root, fixtureRoot, assetsDir);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    static void NormalizePaths(JsonNode? node, string fixtureRoot, string assetsDir)
    {
        if (node is null) return;

        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    if (value is JsonValue jv && jv.TryGetValue<string>(out var text))
                        obj[key] = NormalizePathString(text, fixtureRoot, assetsDir);
                    else
                        NormalizePaths(value, fixtureRoot, assetsDir);
                }
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonValue jv && jv.TryGetValue<string>(out var text))
                        arr[i] = NormalizePathString(text, fixtureRoot, assetsDir);
                    else
                        NormalizePaths(arr[i], fixtureRoot, assetsDir);
                }
                break;
        }
    }

    static string NormalizePathString(string value, string fixtureRoot, string assetsDir)
    {
        if (value.StartsWith(fixtureRoot, StringComparison.OrdinalIgnoreCase))
            return GoldenProjectPath + value[fixtureRoot.Length..].Replace('\\', '/');

        if (value.StartsWith(assetsDir, StringComparison.OrdinalIgnoreCase))
            return GoldenProjectPath + "/Assets" + value[assetsDir.Length..].Replace('\\', '/');

        return value;
    }

    static bool IsUpdateRequested()
        => string.Equals(Environment.GetEnvironmentVariable(UpdateEnvVar), "1", StringComparison.Ordinal);

    static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
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
        proc.WaitForExit(120_000);
        return (proc.ExitCode, stdout, stderr);
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
