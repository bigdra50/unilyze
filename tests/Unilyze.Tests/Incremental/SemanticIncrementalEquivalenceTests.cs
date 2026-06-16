using System.Text.Json.Nodes;
using Unilyze.Tests.Helpers;

namespace Unilyze.Tests.Incremental;

// At a semantic level the incremental path reuses cached per-type cohesion/smell payloads for
// unchanged types. These tests assert that the reused result is byte-identical (after
// normalizing timestamps/tool version) to a clean full analysis, across the mutation kinds that
// exercise cross-tree resolution — body-only edits (fast path) and structural changes (full
// re-enrich fallback). The fixture's Gamma both inherits Delta and calls Delta.Seed() from a
// method body, so a Delta surface change is a body-caller hazard the declaration graph misses.
public sealed class SemanticIncrementalEquivalenceTests : IDisposable
{
    readonly string _projectRoot;
    static readonly string CurrentTargetFramework = ResolveCurrentTargetFramework();
    static readonly string DotnetHostPath = ResolveDotnetHostPath();
    static readonly string AppDllPath = ResolveAppDllPath();

    public SemanticIncrementalEquivalenceTests()
    {
        _projectRoot = Path.Combine(Path.GetTempPath(), $"unilyze-sem-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectRoot);
        WriteInitialProject();
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectRoot))
            Directory.Delete(_projectRoot, true);
    }

    [Theory]
    [InlineData("body-only")]
    [InlineData("signature-change")]
    [InlineData("base-class-change")]
    [InlineData("global-using-add")]
    [InlineData("global-using-modify")]
    [InlineData("add")]
    [InlineData("delete")]
    [InlineData("comment-touch")]
    [InlineData("threshold")]
    [InlineData("define")]
    public void Mutations_StaySemanticEquivalentToFullRun(string mutation)
    {
        Analyze(incremental: true); // seed cache
        ApplyMutation(mutation);
        var incremental = Analyze(incremental: true);
        var full = Analyze(incremental: false);
        Assert.Equal(Normalize(full), Normalize(incremental));
    }

    [Fact]
    public void BodyOnlyEdit_ReEnrichesOnlyTheEditedType()
    {
        Analyze(incremental: true); // seed cache
        ApplyMutation("body-only"); // edits Delta's method body only
        var (exitCode, _, stderr) = Run("-p", _projectRoot, "--level", "core", "-f", "json", "--incremental");

        Assert.Equal(0, exitCode);
        Assert.Contains("[incremental] re-enrich types: 1/", stderr);
        Assert.Contains("[incremental] cache hit:", stderr);
        Assert.DoesNotContain("[incremental] full re-enrich:", stderr);
    }

    [Fact]
    public void SignatureChange_ForcesFullReEnrich()
    {
        Analyze(incremental: true); // seed cache
        ApplyMutation("signature-change"); // changes Delta's public surface
        var (exitCode, _, stderr) = Run("-p", _projectRoot, "--level", "core", "-f", "json", "--incremental");

        Assert.Equal(0, exitCode);
        Assert.Contains("[incremental] full re-enrich:", stderr);
    }

    void WriteInitialProject()
    {
        File.WriteAllText(Path.Combine(_projectRoot, "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "Gamma.cs"), """
            namespace Sample;

            public class Gamma : Delta
            {
                public int Compute() => Helper(2) + Seed();
                int Helper(int x) => x * 2;
            }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "Delta.cs"), """
            namespace Sample;

            public class Delta
            {
                public int Seed() => 7;
            }
            """);
    }

    void ApplyMutation(string mutation)
    {
        switch (mutation)
        {
            case "body-only":
                File.WriteAllText(Path.Combine(_projectRoot, "Delta.cs"), """
                    namespace Sample;

                    public class Delta
                    {
                        public int Seed() => 7 + 0;
                    }
                    """);
                break;
            case "signature-change":
                File.WriteAllText(Path.Combine(_projectRoot, "Delta.cs"), """
                    namespace Sample;

                    public class Delta
                    {
                        public long Seed() => 7;
                        public int Extra() => 1;
                    }
                    """);
                break;
            case "base-class-change":
                File.WriteAllText(Path.Combine(_projectRoot, "Delta.cs"), """
                    namespace Sample;

                    public class Origin { }

                    public class Delta : Origin
                    {
                        public int Seed() => 7;
                    }
                    """);
                break;
            case "global-using-add":
                File.WriteAllText(Path.Combine(_projectRoot, "GlobalUsings.cs"),
                    "global using System.Text;\n");
                break;
            case "global-using-modify":
                File.WriteAllText(Path.Combine(_projectRoot, "GlobalUsings.cs"),
                    "global using System.Text;\n");
                Analyze(incremental: true); // re-seed so the modify (below) compares against a stored set
                File.WriteAllText(Path.Combine(_projectRoot, "GlobalUsings.cs"),
                    "global using System.Text;\nglobal using System.Linq;\n");
                break;
            case "add":
                File.WriteAllText(Path.Combine(_projectRoot, "Added.cs"), """
                    namespace Sample;
                    public class Added { }
                    """);
                break;
            case "delete":
                File.Delete(Path.Combine(_projectRoot, "Gamma.cs"));
                break;
            case "comment-touch":
                File.AppendAllText(Path.Combine(_projectRoot, "Delta.cs"), "\n// touch\n");
                break;
            case "threshold":
                File.WriteAllText(Path.Combine(_projectRoot, ".unilyze.json"), """
                    { "smells": { "godClass": { "linesWarning": 9999 } } }
                    """);
                break;
            case "define":
                File.WriteAllText(Path.Combine(_projectRoot, "App.csproj"), """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net8.0</TargetFramework>
                        <DefineConstants>SEMANTIC_INCREMENTAL_TEST</DefineConstants>
                      </PropertyGroup>
                    </Project>
                    """);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    string Analyze(bool incremental)
    {
        var args = new List<string> { "-p", _projectRoot, "--level", "core", "-f", "json" };
        if (incremental)
            args.Add("--incremental");
        var (exitCode, stdout, _) = Run(args.ToArray());
        Assert.Equal(0, exitCode);
        return stdout;
    }

    static string Normalize(string json)
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

        return root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo { FileName = DotnetHostPath };
        psi.ArgumentList.Add(AppDllPath);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        return TestProcessRunner.Run(psi, 120_000);
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
