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
    [InlineData("alias-retarget")]
    [InlineData("using-retarget")]
    [InlineData("using-reorder")]
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

    // Regression coverage for the pre-#222-merge correctness hole (design doc §2): an alias
    // retarget in AliasHost.cs changes AliasX's real base chain without touching AliasX's
    // declaration signature (base type text is still "A" either way), so
    // StructuralChangeDetector alone classifies this body-only. AliasDependent.cs (unchanged,
    // cached) walks AliasX's base chain for DIT — if the using change is not independently
    // detected, its cached DIT goes stale and full/incremental diverge.
    [Fact]
    public void AliasRetarget_ForcesFullReEnrich()
    {
        Analyze(incremental: true); // seed cache
        ApplyMutation("alias-retarget");
        var (exitCode, _, stderr) = Run("-p", _projectRoot, "--level", "core", "-f", "json", "--incremental");

        Assert.Equal(0, exitCode);
        Assert.Contains("[incremental] full re-enrich: using directives changed in AliasHost.cs", stderr);
    }

    // Same hazard as AliasRetarget_ForcesFullReEnrich, but for a plain `using Ns;` retarget that
    // changes which same-named type an unqualified base-type reference resolves to.
    [Fact]
    public void PlainUsingRetarget_ForcesFullReEnrich()
    {
        Analyze(incremental: true); // seed cache
        ApplyMutation("using-retarget");
        var (exitCode, _, stderr) = Run("-p", _projectRoot, "--level", "core", "-f", "json", "--incremental");

        Assert.Equal(0, exitCode);
        Assert.Contains("[incremental] full re-enrich: using directives changed in PlainHost.cs", stderr);
    }

    // Reordering/whitespace-only edits to a file's usings must not be classified as a using
    // change (the hash is order-insensitive and whitespace-normalized) — confirms the fix does
    // not regress the body-only fast path for the common "goimports/usort ran" edit.
    [Fact]
    public void UsingReorder_StaysOnBodyOnlyFastPath()
    {
        Analyze(incremental: true); // seed cache
        ApplyMutation("using-reorder");
        var (exitCode, _, stderr) = Run("-p", _projectRoot, "--level", "core", "-f", "json", "--incremental");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("[incremental] full re-enrich:", stderr);
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

        // Alias-retarget fixture (design doc §2): AliasHost.cs aliases A to a shallow base
        // (Bar); AliasDependent.cs (never touched by the alias-retarget mutation) inherits
        // AliasHost's AliasX and so its cached DIT depends on AliasX's real base chain.
        File.WriteAllText(Path.Combine(_projectRoot, "AliasBases.cs"), """
            namespace Sample.Ns1
            {
                public class Bar { }
            }

            namespace Sample.Ns2
            {
                public class QuxBase { }

                public class Qux : QuxBase { }
            }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "AliasHost.cs"), """
            using A = Sample.Ns1.Bar;

            namespace Sample;

            public class AliasX : A { }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "AliasDependent.cs"), """
            namespace Sample;

            public class AliasG : AliasX { }
            """);

        // Plain-using-retarget fixture: same hazard, but via an unqualified name (PlainBase)
        // that resolves to a different namespace's type depending on which using is in scope.
        File.WriteAllText(Path.Combine(_projectRoot, "PlainBases.cs"), """
            namespace Sample.NsP1
            {
                public class PlainBase { }
            }

            namespace Sample.NsP2
            {
                public class PlainBaseRoot { }

                public class PlainBase : PlainBaseRoot { }
            }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "PlainHost.cs"), """
            using System;
            using Sample.NsP1;

            namespace Sample;

            public class PlainY : PlainBase { }
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "PlainDependent.cs"), """
            namespace Sample;

            public class PlainH : PlainY { }
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
            case "alias-retarget":
                // Retarget alias A from Ns1.Bar (DIT depth 1) to Ns2.Qux (DIT depth 2).
                // AliasX's declaration signature (base type text "A") is unchanged, so this is
                // only visible as a per-file using-directive change in AliasHost.cs.
                File.WriteAllText(Path.Combine(_projectRoot, "AliasHost.cs"), """
                    using A = Sample.Ns2.Qux;

                    namespace Sample;

                    public class AliasX : A { }
                    """);
                break;
            case "using-retarget":
                // Retarget the unqualified PlainBase resolution from NsP1 (DIT depth 0) to
                // NsP2 (DIT depth 1) by swapping which namespace is imported.
                File.WriteAllText(Path.Combine(_projectRoot, "PlainHost.cs"), """
                    using System;
                    using Sample.NsP2;

                    namespace Sample;

                    public class PlainY : PlainBase { }
                    """);
                break;
            case "using-reorder":
                // Same usings as the seeded fixture, just reordered and with extra whitespace —
                // must normalize/sort to the same hash as the original, so this stays body-only.
                File.WriteAllText(Path.Combine(_projectRoot, "PlainHost.cs"), """
                    using   Sample.NsP1;
                    using System;

                    namespace Sample;

                    public class PlainY : PlainBase { }
                    """);
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
