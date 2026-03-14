using System.Text.Json;
using System.Text.Json.Nodes;

namespace Unilyze.Tests;

public class DiffCalculatorTests
{
    static TypeMetrics MakeTypeMetrics(
        string typeName = "TestClass",
        string ns = "TestNs",
        string assembly = "TestAssembly",
        int lineCount = 100,
        int methodCount = 5,
        double avgCogCC = 3.0,
        int maxCogCC = 5,
        double avgCycCC = 3.0,
        int maxCycCC = 5,
        int maxNestingDepth = 2,
        int excessiveParams = 0,
        double codeHealth = 8.0,
        double? lcom = null,
        IReadOnlyList<MethodMetrics>? methods = null,
        IReadOnlyList<CodeSmell>? smells = null,
        string? qualifiedName = null,
        string? typeId = null)
    {
        methods ??= [];
        return new TypeMetrics(
            typeName, ns, assembly,
            lineCount, methodCount, maxNestingDepth,
            avgCogCC, maxCogCC, avgCycCC, maxCycCC,
            excessiveParams, codeHealth,
            methods,
            Lcom: lcom,
            CodeSmells: smells,
            QualifiedName: qualifiedName ?? (string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}"),
            TypeId: typeId ?? (string.IsNullOrEmpty(ns) ? $"{assembly}::{typeName}" : $"{assembly}::{ns}.{typeName}"));
    }

    static AnalysisResult MakeResult(
        IReadOnlyList<TypeMetrics>? typeMetrics = null,
        string projectPath = "/project")
    {
        return new AnalysisResult(
            projectPath,
            DateTimeOffset.UtcNow,
            [], [], [],
            typeMetrics);
    }

    // --- Basic classification ---

    [Fact]
    public void IdenticalResults_AllUnchanged()
    {
        var metrics = new[] { MakeTypeMetrics() };
        var before = MakeResult(metrics);
        var after = MakeResult(metrics);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(0, diff.Summary.ImprovedCount);
        Assert.Equal(0, diff.Summary.DegradedCount);
        Assert.Equal(1, diff.Summary.UnchangedCount);
        Assert.Empty(diff.Improved);
        Assert.Empty(diff.Degraded);
        Assert.Single(diff.Unchanged);
    }

    [Fact]
    public void ImprovedType_LowerCogCC()
    {
        var before = MakeResult([MakeTypeMetrics(avgCogCC: 10.0, maxCogCC: 15)]);
        var after = MakeResult([MakeTypeMetrics(avgCogCC: 5.0, maxCogCC: 8)]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.ImprovedCount);
        Assert.Single(diff.Improved);
        Assert.Equal(ChangeStatus.Improved, diff.Improved[0].Status);
    }

    [Fact]
    public void DegradedType_HigherCogCC()
    {
        var before = MakeResult([MakeTypeMetrics(avgCogCC: 3.0, maxCogCC: 5)]);
        var after = MakeResult([MakeTypeMetrics(avgCogCC: 10.0, maxCogCC: 20)]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.DegradedCount);
        Assert.Single(diff.Degraded);
        Assert.Equal(ChangeStatus.Degraded, diff.Degraded[0].Status);
    }

    [Fact]
    public void MixedDeltas_AnyDegradation_IsDegraded()
    {
        // CodeHealth improved, but CogCC degraded
        var before = MakeResult([MakeTypeMetrics(codeHealth: 6.0, avgCogCC: 3.0)]);
        var after = MakeResult([MakeTypeMetrics(codeHealth: 9.0, avgCogCC: 8.0)]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.DegradedCount);
        Assert.Single(diff.Degraded);
    }

    [Fact]
    public void ImprovedType_HigherCodeHealth()
    {
        var before = MakeResult([MakeTypeMetrics(codeHealth: 5.0)]);
        var after = MakeResult([MakeTypeMetrics(codeHealth: 9.0)]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.ImprovedCount);
        Assert.Single(diff.Improved);
    }

    // --- Type addition/removal ---

    [Fact]
    public void AddedType_InAddedGroup()
    {
        var before = MakeResult([]);
        var after = MakeResult([MakeTypeMetrics(typeName: "NewClass")]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.AddedCount);
        Assert.Single(diff.Added);
        Assert.Equal("TestNs.NewClass", diff.Added[0].TypeKey);
    }

    [Fact]
    public void RemovedType_InRemovedGroup()
    {
        var before = MakeResult([MakeTypeMetrics(typeName: "OldClass")]);
        var after = MakeResult([]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.RemovedCount);
        Assert.Single(diff.Removed);
        Assert.Equal("TestNs.OldClass", diff.Removed[0].TypeKey);
    }

    // --- Method-level diffs ---

    [Fact]
    public void MethodImproved_TracksMethodDelta()
    {
        var beforeMethods = new[]
        {
            new MethodMetrics("ProcessInput", 15, 8, 4, 2, 30)
        };
        var afterMethods = new[]
        {
            new MethodMetrics("ProcessInput", 7, 4, 2, 2, 20)
        };
        var before = MakeResult([MakeTypeMetrics(
            avgCogCC: 15.0, maxCogCC: 15,
            avgCycCC: 8.0, maxCycCC: 8,
            methods: beforeMethods)]);
        var after = MakeResult([MakeTypeMetrics(
            avgCogCC: 7.0, maxCogCC: 7,
            avgCycCC: 4.0, maxCycCC: 4,
            methods: afterMethods)]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Single(diff.Improved);
        var typeDiff = diff.Improved[0];
        Assert.Single(typeDiff.MethodDiffs);
        var methodDiff = typeDiff.MethodDiffs[0];
        Assert.Equal("ProcessInput", methodDiff.MethodName);
        Assert.Equal(ChangeStatus.Improved, methodDiff.Status);

        var cogCCDelta = methodDiff.IntDeltas.First(d => d.Name == "CognitiveComplexity");
        Assert.Equal(15, cogCCDelta.Before);
        Assert.Equal(7, cogCCDelta.After);
        Assert.Equal(-8, cogCCDelta.Delta);
    }

    // --- Smell changes ---

    [Fact]
    public void SmellResolved_ShowsAsResolved()
    {
        var smell = new CodeSmell(CodeSmellKind.HighComplexity, SmellSeverity.Warning, "TestClass", "ProcessInput", "High complexity");
        var before = MakeResult([MakeTypeMetrics(
            avgCogCC: 15.0, maxCogCC: 15,
            smells: [smell])]);
        var after = MakeResult([MakeTypeMetrics(
            avgCogCC: 5.0, maxCogCC: 5)]);

        var diff = DiffCalculator.Compare(before, after);

        var typeDiff = diff.Improved.Concat(diff.Degraded).Concat(diff.Unchanged).First();
        Assert.NotNull(typeDiff.SmellChanges);
        Assert.Contains(typeDiff.SmellChanges, sc => sc.IsResolved && sc.Smell.Kind == CodeSmellKind.HighComplexity);
    }

    [Fact]
    public void SmellAdded_ShowsAsAdded()
    {
        var smell = new CodeSmell(CodeSmellKind.GodClass, SmellSeverity.Critical, "TestClass", null, "God class detected");
        var before = MakeResult([MakeTypeMetrics()]);
        var after = MakeResult([MakeTypeMetrics(lineCount: 600, smells: [smell])]);

        var diff = DiffCalculator.Compare(before, after);

        var typeDiff = diff.Improved.Concat(diff.Degraded).Concat(diff.Unchanged).First();
        Assert.NotNull(typeDiff.SmellChanges);
        Assert.Contains(typeDiff.SmellChanges, sc => !sc.IsResolved && sc.Smell.Kind == CodeSmellKind.GodClass);
    }

    // --- Edge cases ---

    [Fact]
    public void EmptyTypeMetrics_EmptyDiff()
    {
        var before = MakeResult();
        var after = MakeResult();

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(0, diff.Summary.ImprovedCount);
        Assert.Equal(0, diff.Summary.DegradedCount);
        Assert.Equal(0, diff.Summary.UnchangedCount);
        Assert.Equal(0, diff.Summary.AddedCount);
        Assert.Equal(0, diff.Summary.RemovedCount);
    }

    [Fact]
    public void NamespaceMatching_NoCollision()
    {
        var before = MakeResult([
            MakeTypeMetrics(typeName: "Service", ns: "App.Domain"),
            MakeTypeMetrics(typeName: "Service", ns: "App.Infra"),
        ]);
        var after = MakeResult([
            MakeTypeMetrics(typeName: "Service", ns: "App.Domain", avgCogCC: 1.0, maxCogCC: 1),
            MakeTypeMetrics(typeName: "Service", ns: "App.Infra"),
        ]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.ImprovedCount);
        Assert.Equal(1, diff.Summary.UnchangedCount);
        Assert.Equal("App.Domain.Service", diff.Improved[0].TypeKey);
    }

    [Fact]
    public void TypeIdMatching_AvoidsAssemblyCollision()
    {
        var before = MakeResult([
            MakeTypeMetrics(typeName: "Service", ns: "App.Core", assembly: "AsmA", typeId: "AsmA::App.Core.Service"),
            MakeTypeMetrics(typeName: "Service", ns: "App.Core", assembly: "AsmB", typeId: "AsmB::App.Core.Service"),
        ]);
        var after = MakeResult([
            MakeTypeMetrics(typeName: "Service", ns: "App.Core", assembly: "AsmA", avgCogCC: 1.0, maxCogCC: 1, typeId: "AsmA::App.Core.Service"),
            MakeTypeMetrics(typeName: "Service", ns: "App.Core", assembly: "AsmB", typeId: "AsmB::App.Core.Service"),
        ]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.ImprovedCount);
        Assert.Single(diff.Improved);
        Assert.Equal("AsmA", diff.Improved[0].Assembly);
        Assert.Equal(1, diff.Summary.UnchangedCount);
    }

    // --- JSON serialization ---

    [Fact]
    public void SerializesToJson_WithGrouping()
    {
        var before = MakeResult([MakeTypeMetrics(avgCogCC: 10.0, maxCogCC: 15)], "/before");
        var after = MakeResult([MakeTypeMetrics(avgCogCC: 5.0, maxCogCC: 8)], "/after");

        var diff = DiffCalculator.Compare(before, after);
        var json = JsonSerializer.Serialize(diff, AnalysisJsonContext.Default.DiffResult);
        var doc = JsonNode.Parse(json)!;

        Assert.Equal("/before", doc["beforePath"]!.GetValue<string>());
        Assert.Equal("/after", doc["afterPath"]!.GetValue<string>());
        Assert.NotNull(doc["summary"]);
        Assert.Equal(1, doc["summary"]!["improvedCount"]!.GetValue<int>());
        Assert.NotNull(doc["improved"]);
        Assert.Single(doc["improved"]!.AsArray());
    }

    [Fact]
    public void ChangeStatus_SerializedAsString()
    {
        var before = MakeResult([MakeTypeMetrics(avgCogCC: 10.0, maxCogCC: 15)]);
        var after = MakeResult([MakeTypeMetrics(avgCogCC: 5.0, maxCogCC: 8)]);

        var diff = DiffCalculator.Compare(before, after);
        var json = JsonSerializer.Serialize(diff, AnalysisJsonContext.Default.DiffResult);
        var doc = JsonNode.Parse(json)!;

        var status = doc["improved"]!.AsArray()[0]!["status"]!.GetValue<string>();
        Assert.Equal("Improved", status);
    }

    // --- MI higher-is-better ---

    [Fact]
    public void ImprovedType_HigherAverageMI()
    {
        var before = MakeResult([MakeTypeMetrics() with { AverageMaintainabilityIndex = 60.0 }]);
        var after = MakeResult([MakeTypeMetrics() with { AverageMaintainabilityIndex = 80.0 }]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.ImprovedCount);
        Assert.Single(diff.Improved);
        var delta = diff.Improved[0].DoubleDeltas.First(d => d.Name == "AverageMaintainabilityIndex");
        Assert.Equal(60.0, delta.Before);
        Assert.Equal(80.0, delta.After);
        Assert.Equal(20.0, delta.Delta);
    }

    [Fact]
    public void ImprovedType_HigherMinMI()
    {
        var before = MakeResult([MakeTypeMetrics() with { MinMaintainabilityIndex = 40.0 }]);
        var after = MakeResult([MakeTypeMetrics() with { MinMaintainabilityIndex = 55.0 }]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.ImprovedCount);
        Assert.Single(diff.Improved);
        var delta = diff.Improved[0].DoubleDeltas.First(d => d.Name == "MinMaintainabilityIndex");
        Assert.Equal(40.0, delta.Before);
        Assert.Equal(55.0, delta.After);
        Assert.Equal(15.0, delta.Delta);
    }

    [Fact]
    public void DegradedType_LowerMI()
    {
        var before = MakeResult([MakeTypeMetrics() with { AverageMaintainabilityIndex = 80.0 }]);
        var after = MakeResult([MakeTypeMetrics() with { AverageMaintainabilityIndex = 60.0 }]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.DegradedCount);
        Assert.Single(diff.Degraded);
        var delta = diff.Degraded[0].DoubleDeltas.First(d => d.Name == "AverageMaintainabilityIndex");
        Assert.Equal(-20.0, delta.Delta);
    }

    // --- Lcom lower-is-better ---

    [Fact]
    public void ImprovedType_LowerLcom()
    {
        var before = MakeResult([MakeTypeMetrics(lcom: 0.8)]);
        var after = MakeResult([MakeTypeMetrics(lcom: 0.3)]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.ImprovedCount);
        Assert.Single(diff.Improved);
        var delta = diff.Improved[0].DoubleDeltas.First(d => d.Name == "Lcom");
        Assert.Equal(0.8, delta.Before);
        Assert.Equal(0.3, delta.After);
        Assert.True(delta.Delta < 0);
    }

    [Fact]
    public void DegradedType_HigherLcom()
    {
        var before = MakeResult([MakeTypeMetrics(lcom: 0.3)]);
        var after = MakeResult([MakeTypeMetrics(lcom: 0.9)]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.DegradedCount);
        Assert.Single(diff.Degraded);
        var delta = diff.Degraded[0].DoubleDeltas.First(d => d.Name == "Lcom");
        Assert.True(delta.Delta > 0);
    }

    // --- Instability lower-is-better ---

    [Fact]
    public void ImprovedType_LowerInstability()
    {
        var before = MakeResult([MakeTypeMetrics() with { Instability = 0.9 }]);
        var after = MakeResult([MakeTypeMetrics() with { Instability = 0.4 }]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.ImprovedCount);
        Assert.Single(diff.Improved);
        var delta = diff.Improved[0].DoubleDeltas.First(d => d.Name == "Instability");
        Assert.Equal(0.9, delta.Before);
        Assert.Equal(0.4, delta.After);
        Assert.True(delta.Delta < 0);
    }

    // --- Optional metrics: null handling ---

    [Fact]
    public void OptionalMetrics_NullOnBothSides_NoEffect()
    {
        // Lcom, Cbo both null on both sides → Unchanged
        var before = MakeResult([MakeTypeMetrics()]);
        var after = MakeResult([MakeTypeMetrics()]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.UnchangedCount);
        Assert.Empty(diff.Improved);
        Assert.Empty(diff.Degraded);
        // No Lcom delta should be present
        var typeDiff = diff.Unchanged[0];
        Assert.DoesNotContain(typeDiff.DoubleDeltas, d => d.Name == "Lcom");
        Assert.DoesNotContain(typeDiff.IntDeltas, d => d.Name == "Cbo");
    }

    [Fact]
    public void OptionalMetrics_NullOnOneSide_NotIncluded()
    {
        // before has Lcom, after does not → delta not generated
        var before = MakeResult([MakeTypeMetrics(lcom: 0.5)]);
        var after = MakeResult([MakeTypeMetrics(lcom: null)]);

        var diff = DiffCalculator.Compare(before, after);

        var allDiffs = diff.Improved.Concat(diff.Degraded).Concat(diff.Unchanged).ToList();
        Assert.Single(allDiffs);
        Assert.DoesNotContain(allDiffs[0].DoubleDeltas, d => d.Name == "Lcom");
    }

    // --- Int deltas: Cbo and Dit ---

    [Fact]
    public void IntDeltaForCbo_DecreasedIsImproved()
    {
        var before = MakeResult([MakeTypeMetrics() with { Cbo = 10 }]);
        var after = MakeResult([MakeTypeMetrics() with { Cbo = 4 }]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.ImprovedCount);
        Assert.Single(diff.Improved);
        var delta = diff.Improved[0].IntDeltas.First(d => d.Name == "Cbo");
        Assert.Equal(10, delta.Before);
        Assert.Equal(4, delta.After);
        Assert.Equal(-6, delta.Delta);
    }

    [Fact]
    public void IntDeltaForDit_IncreasedIsDegraded()
    {
        var before = MakeResult([MakeTypeMetrics() with { Dit = 1 }]);
        var after = MakeResult([MakeTypeMetrics() with { Dit = 5 }]);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(1, diff.Summary.DegradedCount);
        Assert.Single(diff.Degraded);
        var delta = diff.Degraded[0].IntDeltas.First(d => d.Name == "Dit");
        Assert.Equal(1, delta.Before);
        Assert.Equal(5, delta.After);
        Assert.Equal(4, delta.Delta);
    }

    // --- Null TypeMetrics ---

    [Fact]
    public void NullTypeMetrics_EmptyDiff()
    {
        var before = new AnalysisResult("/project", DateTimeOffset.UtcNow, [], [], [], TypeMetrics: null);
        var after = new AnalysisResult("/project", DateTimeOffset.UtcNow, [], [], [], TypeMetrics: null);

        var diff = DiffCalculator.Compare(before, after);

        Assert.Equal(0, diff.Summary.ImprovedCount);
        Assert.Equal(0, diff.Summary.DegradedCount);
        Assert.Equal(0, diff.Summary.UnchangedCount);
        Assert.Equal(0, diff.Summary.AddedCount);
        Assert.Equal(0, diff.Summary.RemovedCount);
    }
}
