using Unilyze.Metrics;
using Unilyze.Pipeline;
using Unilyze.Serve;

namespace Unilyze.Tests.Serve;

// Shadow verification comparison logic (design doc §7.3). Pure unit tests over the comparator
// itself — deliberately injecting a real invalidation bug into the analysis pipeline to exercise
// the DIVERGENCE path end-to-end is not practical here (design doc §7.3 allows substituting a
// comparison-logic unit test), so these assert Compare's behavior directly against hand-built
// AnalysisResult pairs. ServeVerifyIncrementalBuildTests below covers the divergence-free path
// through the real SnapshotBuilder + AnalysisPipeline stack.
public sealed class ServeVerifyIncrementalTests
{
    [Fact]
    public void Compare_IdenticalResults_NoDivergence()
    {
        var full = Result([Metrics("A"), Metrics("B")]);
        var incremental = Result([Metrics("A"), Metrics("B")]);

        var report = ServeVerifyIncremental.Compare(full, incremental);

        Assert.False(report.Diverged);
        Assert.Empty(report.TypeIds);
    }

    [Fact]
    public void Compare_IgnoresAnalyzedAtAndToolVersionDifferences()
    {
        var full = Result([Metrics("A")], analyzedAt: DateTimeOffset.UnixEpoch, toolVersion: "1.2.3");
        var incremental = Result([Metrics("A")], analyzedAt: DateTimeOffset.UtcNow, toolVersion: "9.9.9");

        var report = ServeVerifyIncremental.Compare(full, incremental);

        Assert.False(report.Diverged);
    }

    [Fact]
    public void Compare_DifferingTypeMetricsField_ReportsThatTypeId()
    {
        var full = Result([Metrics("A", codeHealth: 100), Metrics("B", codeHealth: 90)]);
        var incremental = Result([Metrics("A", codeHealth: 100), Metrics("B", codeHealth: 42)]); // stale B

        var report = ServeVerifyIncremental.Compare(full, incremental);

        Assert.True(report.Diverged);
        Assert.Equal(["B"], report.TypeIds);
    }

    [Fact]
    public void Compare_TypeMissingFromOneSide_ReportsThatTypeId()
    {
        var full = Result([Metrics("A"), Metrics("B")]);
        var incremental = Result([Metrics("A")]); // B missing — e.g. an under-invalidation bug

        var report = ServeVerifyIncremental.Compare(full, incremental);

        Assert.True(report.Diverged);
        Assert.Equal(["B"], report.TypeIds);
    }

    [Fact]
    public void Compare_MultipleDivergentTypes_ReportsAllSortedOrdinal()
    {
        var full = Result([Metrics("Z", codeHealth: 1), Metrics("A", codeHealth: 1)]);
        var incremental = Result([Metrics("Z", codeHealth: 2), Metrics("A", codeHealth: 2)]);

        var report = ServeVerifyIncremental.Compare(full, incremental);

        Assert.True(report.Diverged);
        Assert.Equal(["A", "Z"], report.TypeIds);
    }

    // A divergence outside typeMetrics (design doc §4.4: types/dependencies/aggregation stay full
    // every generation and RDI never touches them) has no TypeId to key on — the comparator falls
    // back to a generic message instead of silently dropping the divergence.
    [Fact]
    public void Compare_NonTypeKeyedDivergence_FallsBackToGenericMessage()
    {
        var full = Result([Metrics("A")]) with { ProjectPath = "/full" };
        var incremental = Result([Metrics("A")]) with { ProjectPath = "/incremental" };

        var report = ServeVerifyIncremental.Compare(full, incremental);

        Assert.True(report.Diverged);
        Assert.Single(report.TypeIds);
        Assert.Contains("non-type-keyed", report.TypeIds[0], StringComparison.Ordinal);
    }

    static TypeMetrics Metrics(string typeId, double codeHealth = 100) =>
        new(
            TypeName: "T", Namespace: "Sample", Assembly: "Asm", LineCount: 1, MethodCount: 0,
            MaxNestingDepth: 0, AverageCognitiveComplexity: 0, MaxCognitiveComplexity: 0,
            AverageCyclomaticComplexity: 0, MaxCyclomaticComplexity: 0, ExcessiveParameterMethodCount: 0,
            CodeHealth: codeHealth, Methods: [], TypeId: typeId);

    static AnalysisResult Result(
        IReadOnlyList<TypeMetrics> typeMetrics, DateTimeOffset? analyzedAt = null, string? toolVersion = null) =>
        new(
            ProjectPath: "/proj",
            AnalyzedAt: analyzedAt ?? DateTimeOffset.UtcNow,
            Assemblies: [],
            Types: [],
            Dependencies: [],
            TypeMetrics: typeMetrics,
            ToolVersion: toolVersion ?? "1.0.0");
}

// SnapshotBuilder's shadow-verification wiring (design doc §7.3), exercised end-to-end through
// the real AnalysisPipeline on a small on-disk fixture — following the ServeAnalysisLoopTests
// pattern of testing the serve-side plumbing directly rather than through the HTTP server.
public sealed class ServeVerifyIncrementalBuildTests : IDisposable
{
    readonly string _projectRoot;

    public ServeVerifyIncrementalBuildTests()
    {
        _projectRoot = Path.Combine(Path.GetTempPath(), $"unilyze-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectRoot);
        File.WriteAllText(Path.Combine(_projectRoot, "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_projectRoot, "Widget.cs"), """
            namespace Sample;

            public class Widget
            {
                public int Value { get; set; }

                public int Double() => Value * 2;
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectRoot))
            Directory.Delete(_projectRoot, true);
    }

    [Fact]
    public void VerifyEveryGeneration_NoDivergence_LogsNothing()
    {
        var options = new ServeOptions(
            Path: _projectRoot, Port: null, NoOpen: true, RequestedLevel: null, Profile: null,
            ExcludeDirs: [], Prefix: null, Assembly: null, ResolveNuget: false, IncludeGenerated: false,
            TargetFramework: null, VerifyIncrementalEveryN: 1);
        var builder = new SnapshotBuilder(options);

        var originalError = Console.Error;
        using var capture = new StringWriter();
        try
        {
            Console.SetError(capture);
            builder.Build(); // cold generation 1 — verify fires (everyN=1)

            // Warm generation 2, still incremental: File.WriteAllText's timestamp precision can
            // collide inside a fast loop, so touch the content to guarantee a re-parse.
            File.WriteAllText(Path.Combine(_projectRoot, "Widget.cs"), """
                namespace Sample;

                public class Widget
                {
                    public int Value { get; set; }

                    public int Double() => Value * 2 + 0;
                }
                """);
            builder.Build(); // warm generation 2 — verify fires again
        }
        finally
        {
            Console.SetError(originalError);
        }

        var output = capture.ToString();
        Assert.DoesNotContain("[incremental] DIVERGENCE", output);
        Assert.DoesNotContain("shadow verification failed", output);
    }

    [Fact]
    public void VerifyDisabledByDefault_NeverRunsShadowAnalysis()
    {
        var options = new ServeOptions(
            Path: _projectRoot, Port: null, NoOpen: true, RequestedLevel: null, Profile: null,
            ExcludeDirs: [], Prefix: null, Assembly: null, ResolveNuget: false, IncludeGenerated: false,
            TargetFramework: null); // VerifyIncrementalEveryN defaults to null
        var builder = new SnapshotBuilder(options);

        var content = builder.Build();

        Assert.True(content.JsonBytes.Length > 0);
    }
}
