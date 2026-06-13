using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Unilyze.Tests.Helpers;

namespace Unilyze.Tests.Helpers;

internal static class GoldenCorpusTestSupport
{
    internal const string GoldenProjectPath = "/golden-fixture";

    internal static readonly string FixtureRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "golden"));

    internal static readonly string ExpectedPath = Path.Combine(FixtureRoot, "expected.json");
    internal static readonly string CsprojPath = Path.Combine(FixtureRoot, "Golden.csproj");
    internal static readonly string CurrentTargetFramework = ResolveCurrentTargetFramework();

    static readonly string DotnetHostPath = ResolveDotnetHostPath();
    static readonly string AppDllPath = ResolveAppDllPath();

    internal static JsonObject ParseNormalized(string json)
        => JsonNode.Parse(NormalizeForComparison(json))?.AsObject()
           ?? throw new InvalidOperationException("Failed to parse normalized golden JSON.");

    internal static string NormalizeForComparison(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Golden analysis output was not valid JSON.");

        var fixtureRoot = Path.GetFullPath(FixtureRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var assetsDir = Path.Combine(fixtureRoot, "Assets")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        root["projectPath"] = GoldenProjectPath;
        root.Remove("analyzedAt");
        root.Remove("toolVersion");

        NormalizePaths(root, fixtureRoot, assetsDir);
        return Serialize(root);
    }

    internal static string Serialize(JsonNode node)
        => node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;

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

    static void NormalizePaths(JsonNode? node, string fixtureRoot, string assetsDir)
    {
        if (node is null)
            return;

        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
                        obj[key] = NormalizePathString(text, fixtureRoot, assetsDir);
                    else
                        NormalizePaths(value, fixtureRoot, assetsDir);
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
                        array[i] = NormalizePathString(text, fixtureRoot, assetsDir);
                    else
                        NormalizePaths(array[i], fixtureRoot, assetsDir);
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

    static string ResolveCurrentTargetFramework()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var targetFramework = Path.GetFileName(baseDir);
        if (string.IsNullOrWhiteSpace(targetFramework)
            || !targetFramework.StartsWith("net", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Could not infer target framework from base directory: {AppContext.BaseDirectory}");
        }

        return targetFramework;
    }

    static string ResolveDotnetHostPath()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return string.IsNullOrWhiteSpace(configured) ? "dotnet" : configured;
    }

    static string ResolveAppDllPath()
    {
        var path = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "Unilyze",
                "bin",
                "Debug",
                CurrentTargetFramework,
                "Unilyze.dll"));

        if (!File.Exists(path))
            throw new FileNotFoundException($"Could not find CLI assembly under test: {path}", path);

        return path;
    }
}
