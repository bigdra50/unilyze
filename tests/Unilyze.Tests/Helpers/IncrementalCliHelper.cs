using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Unilyze.Tests.Helpers;

// Shared CLI-invocation + JSON-normalization support for the incremental-analysis differential
// tests (IncrementalAnalysisTests, SemanticIncrementalEquivalenceTests, MutationDifferentialTests).
// Extracted so the three suites share one implementation instead of three near-identical copies
// (design doc §7.1: "reuse the existing Normalize helper verbatim"; this repo also dogfoods its
// own clone detector in CI, so duplicating this block three times would be a smell it would flag).
internal static class IncrementalCliHelper
{
    public static readonly string CurrentTargetFramework = ResolveCurrentTargetFramework();
    public static readonly string DotnetHostPath = ResolveDotnetHostPath();
    public static readonly string AppDllPath = ResolveAppDllPath();

    public static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
    {
        var psi = new ProcessStartInfo { FileName = DotnetHostPath };
        psi.ArgumentList.Add(AppDllPath);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        return TestProcessRunner.Run(psi, 120_000);
    }

    // Strips run-to-run noise (timestamp, tool version) and resolves the sourceTable-indexed
    // fileRef indirection back to plain paths, so two analyses of the same source can be compared
    // for byte-identical equality regardless of when/which binary produced them.
    public static string Normalize(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Invalid JSON");
        root.Remove("analyzedAt");
        root.Remove("toolVersion");

        var sourceTable = root["sourceTable"]?.AsArray();
        if (sourceTable is not null)
        {
            var pathByIndex = new Dictionary<int, string>();
            for (var i = 0; i < sourceTable.Count; i++)
                pathByIndex[i] = sourceTable[i]?.GetValue<string>() ?? "";

            void ResolveFileRef(JsonNode? node)
            {
                if (node is JsonObject obj && obj.ContainsKey("fileRef"))
                {
                    var idx = obj["fileRef"]?.GetValue<int>() ?? 0;
                    obj["fileRef"] = pathByIndex.GetValueOrDefault(idx, "");
                }
            }

            if (root["types"]?.AsArray() is { } types)
                foreach (var type in types)
                {
                    if (type?["declarations"]?.AsArray() is { } decls)
                        foreach (var d in decls) ResolveFileRef(d);
                    if (type?["members"]?.AsArray() is { } members)
                        foreach (var m in members)
                            if (m?["location"] is { } loc) ResolveFileRef(loc);
                }
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    static string ResolveCurrentTargetFramework() =>
        Path.GetFileName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!;

    static string ResolveDotnetHostPath() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    static string ResolveAppDllPath()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Unilyze", "bin", "Release", CurrentTargetFramework, "Unilyze.dll"));
        if (!File.Exists(path))
        {
            path = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "Unilyze", "bin", "Debug", CurrentTargetFramework, "Unilyze.dll"));
        }
        return path;
    }
}
