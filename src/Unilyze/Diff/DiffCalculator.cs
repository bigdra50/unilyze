using Unilyze.Detectors;
using Unilyze.Metrics;
using Unilyze.Pipeline;
namespace Unilyze.Diff;

internal static class DiffCalculator
{
    private sealed record DeltaScoreCalculation(double Score, int LowRiskCount, int HighRiskCount);

    const int MethodCognitiveComplexityThreshold = 15;
    const int MethodNestingDepthThreshold = 4;
    const int MethodLineCountThreshold = 80;
    const int TypeCognitiveComplexityThreshold = 15;
    const int TypeNestingDepthThreshold = 4;
    const int TypeLineCountThreshold = 500;

    public static DiffResult Compare(AnalysisResult before, AnalysisResult after)
    {
        var beforeByKey = IndexByTypeKey(before.TypeMetrics);
        var afterByKey = IndexByTypeKey(after.TypeMetrics);

        var allKeys = new HashSet<string>(beforeByKey.Keys);
        allKeys.UnionWith(afterByKey.Keys);

        var improved = new List<TypeDiff>();
        var degraded = new List<TypeDiff>();
        var unchanged = new List<TypeDiff>();
        var added = new List<TypeDiff>();
        var removed = new List<TypeDiff>();

        foreach (var key in allKeys)
        {
            var hasBefore = beforeByKey.TryGetValue(key, out var b);
            var hasAfter = afterByKey.TryGetValue(key, out var a);

            if (!hasBefore) { added.Add(BuildOneSidedTypeDiff(a!, ChangeStatus.Unchanged)); continue; }
            if (!hasAfter) { removed.Add(BuildOneSidedTypeDiff(b!, ChangeStatus.Unchanged)); continue; }

            var diff = ComputeTypeDiff(b!, a!);
            var target = diff.Status switch
            {
                ChangeStatus.Improved => improved,
                ChangeStatus.Degraded => degraded,
                _ => unchanged
            };
            target.Add(diff);
        }

        var summary = new DiffSummary(
            improved.Count, degraded.Count, unchanged.Count,
            added.Count, removed.Count);

        var delta = CalculateDeltaScore(beforeByKey, afterByKey);
        return new DiffResult(
            before.ProjectPath, after.ProjectPath,
            before.AnalyzedAt, after.AnalyzedAt,
            summary,
            delta.Score,
            delta.LowRiskCount,
            delta.HighRiskCount,
            improved, degraded, unchanged, added, removed);
    }

    static DeltaScoreCalculation CalculateDeltaScore(
        IReadOnlyDictionary<string, TypeMetrics> beforeByKey,
        IReadOnlyDictionary<string, TypeMetrics> afterByKey)
    {
        var lowRiskCount = 0;
        var highRiskCount = 0;

        foreach (var (key, after) in afterByKey)
        {
            beforeByKey.TryGetValue(key, out var before);
            CountChangedType(before, after, ref lowRiskCount, ref highRiskCount);
            CountChangedMethods(before?.Methods ?? [], after.Methods, ref lowRiskCount, ref highRiskCount);
        }

        var total = lowRiskCount + highRiskCount;
        var score = total == 0 ? 1.0 : (double)lowRiskCount / total;
        return new DeltaScoreCalculation(score, lowRiskCount, highRiskCount);
    }

    static void CountChangedType(
        TypeMetrics? before,
        TypeMetrics after,
        ref int lowRiskCount,
        ref int highRiskCount)
    {
        if (before != null && HasSameRiskMetrics(before, after))
            return;

        CountRisk(IsHighRisk(after), ref lowRiskCount, ref highRiskCount);
    }

    static void CountChangedMethods(
        IReadOnlyList<MethodMetrics> before,
        IReadOnlyList<MethodMetrics> after,
        ref int lowRiskCount,
        ref int highRiskCount)
    {
        var beforeByKey = new Dictionary<string, List<MethodMetrics>>();
        foreach (var method in before)
        {
            var key = MethodKey(method);
            if (!beforeByKey.TryGetValue(key, out var candidates))
            {
                candidates = [];
                beforeByKey[key] = candidates;
            }
            candidates.Add(method);
        }

        foreach (var method in after)
        {
            var key = MethodKey(method);
            if (!beforeByKey.TryGetValue(key, out var candidates))
            {
                CountRisk(IsHighRisk(method), ref lowRiskCount, ref highRiskCount);
                continue;
            }

            var unchangedIndex = candidates.FindIndex(candidate => HasSameRiskMetrics(candidate, method));
            if (unchangedIndex >= 0)
            {
                candidates.RemoveAt(unchangedIndex);
                continue;
            }

            if (candidates.Count > 0)
                candidates.RemoveAt(0);
            CountRisk(IsHighRisk(method), ref lowRiskCount, ref highRiskCount);
        }
    }

    static bool HasSameRiskMetrics(TypeMetrics before, TypeMetrics after) =>
        before.MaxCognitiveComplexity == after.MaxCognitiveComplexity
        && before.MaxNestingDepth == after.MaxNestingDepth
        && before.LineCount == after.LineCount;

    static bool HasSameRiskMetrics(MethodMetrics before, MethodMetrics after) =>
        before.CognitiveComplexity == after.CognitiveComplexity
        && before.MaxNestingDepth == after.MaxNestingDepth
        && before.LineCount == after.LineCount;

    static bool IsHighRisk(TypeMetrics metrics) =>
        metrics.MaxCognitiveComplexity >= TypeCognitiveComplexityThreshold
        || metrics.MaxNestingDepth >= TypeNestingDepthThreshold
        || metrics.LineCount >= TypeLineCountThreshold;

    static bool IsHighRisk(MethodMetrics metrics) =>
        metrics.CognitiveComplexity >= MethodCognitiveComplexityThreshold
        || metrics.MaxNestingDepth >= MethodNestingDepthThreshold
        || metrics.LineCount >= MethodLineCountThreshold;

    static void CountRisk(bool isHighRisk, ref int lowRiskCount, ref int highRiskCount)
    {
        if (isHighRisk)
            highRiskCount++;
        else
            lowRiskCount++;
    }

    static Dictionary<string, TypeMetrics> IndexByTypeKey(IReadOnlyList<TypeMetrics>? metrics)
    {
        var dict = new Dictionary<string, TypeMetrics>();
        if (metrics is null) return dict;
        foreach (var m in metrics)
            dict.TryAdd(TypeIdentity.GetTypeId(m), m);
        return dict;
    }

    static string TypeKey(string ns, string name) =>
        string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

    static TypeDiff ComputeTypeDiff(TypeMetrics before, TypeMetrics after)
    {
        var key = BuildDisplayTypeKey(after);
        var doubleDeltas = BuildDoubleDeltas(before, after);
        var intDeltas = BuildIntDeltas(before, after);
        var methodDiffs = ComputeMethodDiffs(before.Methods, after.Methods);
        var smellChanges = ComputeSmellChanges(before.CodeSmells, after.CodeSmells);
        var status = ClassifyType(doubleDeltas, intDeltas, smellChanges);

        return new TypeDiff(
            key, after.TypeName, after.Namespace, after.Assembly,
            status, doubleDeltas, intDeltas, methodDiffs, smellChanges);
    }

    static List<MetricDelta<double>> BuildDoubleDeltas(TypeMetrics b, TypeMetrics a)
    {
        var deltas = new List<MetricDelta<double>>
        {
            new("CodeHealth", b.CodeHealth, a.CodeHealth, a.CodeHealth - b.CodeHealth),
            new("AverageCognitiveComplexity", b.AverageCognitiveComplexity, a.AverageCognitiveComplexity, a.AverageCognitiveComplexity - b.AverageCognitiveComplexity),
            new("AverageCyclomaticComplexity", b.AverageCyclomaticComplexity, a.AverageCyclomaticComplexity, a.AverageCyclomaticComplexity - b.AverageCyclomaticComplexity),
        };

        AddDoubleDelta(deltas, "Lcom", b.Lcom, a.Lcom);
        AddDoubleDelta(deltas, "AverageMaintainabilityIndex", b.AverageMaintainabilityIndex, a.AverageMaintainabilityIndex);
        AddDoubleDelta(deltas, "MinMaintainabilityIndex", b.MinMaintainabilityIndex, a.MinMaintainabilityIndex);
        AddDoubleDelta(deltas, "Instability", b.Instability, a.Instability);
        return deltas;
    }

    static void AddDoubleDelta(List<MetricDelta<double>> deltas, string name, double? before, double? after)
    {
        if (before.HasValue && after.HasValue)
            deltas.Add(new(name, before.Value, after.Value, after.Value - before.Value));
    }

    static List<MetricDelta<int>> BuildIntDeltas(TypeMetrics b, TypeMetrics a)
    {
        var deltas = new List<MetricDelta<int>>();
        AddIntDelta(deltas, "Cbo", b.Cbo, a.Cbo);
        AddIntDelta(deltas, "Dit", b.Dit, a.Dit);
        AddIntDelta(deltas, "AfferentCoupling", b.AfferentCoupling, a.AfferentCoupling);
        AddIntDelta(deltas, "EfferentCoupling", b.EfferentCoupling, a.EfferentCoupling);
        deltas.AddRange(new MetricDelta<int>[]
        {
            new("LineCount", b.LineCount, a.LineCount, a.LineCount - b.LineCount),
            new("MethodCount", b.MethodCount, a.MethodCount, a.MethodCount - b.MethodCount),
            new("MaxNestingDepth", b.MaxNestingDepth, a.MaxNestingDepth, a.MaxNestingDepth - b.MaxNestingDepth),
            new("MaxCognitiveComplexity", b.MaxCognitiveComplexity, a.MaxCognitiveComplexity, a.MaxCognitiveComplexity - b.MaxCognitiveComplexity),
            new("MaxCyclomaticComplexity", b.MaxCyclomaticComplexity, a.MaxCyclomaticComplexity, a.MaxCyclomaticComplexity - b.MaxCyclomaticComplexity),
            new("ExcessiveParameterMethodCount", b.ExcessiveParameterMethodCount, a.ExcessiveParameterMethodCount, a.ExcessiveParameterMethodCount - b.ExcessiveParameterMethodCount),
        });
        return deltas;
    }

    static void AddIntDelta(List<MetricDelta<int>> deltas, string name, int? before, int? after)
    {
        if (before.HasValue && after.HasValue)
            deltas.Add(new(name, before.Value, after.Value, after.Value - before.Value));
    }

    static IReadOnlyList<MethodDiff> ComputeMethodDiffs(
        IReadOnlyList<MethodMetrics> before, IReadOnlyList<MethodMetrics> after)
    {
        var afterByKey = new Dictionary<string, MethodMetrics>();
        foreach (var m in after)
            afterByKey.TryAdd(MethodKey(m), m);

        var matched = new HashSet<string>();
        var diffs = new List<MethodDiff>();

        foreach (var b in before)
        {
            var key = MethodKey(b);
            if (afterByKey.TryGetValue(key, out var a))
            {
                matched.Add(key);
                var deltas = BuildMethodDeltas(b, a);
                var status = ClassifyMethodDeltas(deltas);
                diffs.Add(new MethodDiff(a.MethodName, a.ParameterCount, status, deltas,
                    MethodChangeKind.Changed, a.MemberId ?? b.MemberId));
            }
            else
            {
                diffs.Add(new MethodDiff(b.MethodName, b.ParameterCount, ChangeStatus.Unchanged, [],
                    MethodChangeKind.Removed, b.MemberId));
            }
        }

        foreach (var a in after)
        {
            var key = MethodKey(a);
            if (!matched.Contains(key))
            {
                diffs.Add(new MethodDiff(a.MethodName, a.ParameterCount, ChangeStatus.Unchanged, [],
                    MethodChangeKind.Added, a.MemberId));
            }
        }

        return diffs;
    }

    static List<MetricDelta<int>> BuildMethodDeltas(MethodMetrics b, MethodMetrics a) =>
    [
        new("CognitiveComplexity", b.CognitiveComplexity, a.CognitiveComplexity, a.CognitiveComplexity - b.CognitiveComplexity),
        new("CyclomaticComplexity", b.CyclomaticComplexity, a.CyclomaticComplexity, a.CyclomaticComplexity - b.CyclomaticComplexity),
        new("MaxNestingDepth", b.MaxNestingDepth, a.MaxNestingDepth, a.MaxNestingDepth - b.MaxNestingDepth),
        new("LineCount", b.LineCount, a.LineCount, a.LineCount - b.LineCount),
    ];

    static string MethodKey(MethodMetrics m) => m.MemberId ?? $"{m.MethodName}:{m.ParameterCount}";

    static IReadOnlyList<SmellChange>? ComputeSmellChanges(
        IReadOnlyList<CodeSmell>? before, IReadOnlyList<CodeSmell>? after)
    {
        if (before is null && after is null) return null;

        var beforeSmells = (before ?? []).Where(SmellAggregation.CountsForDiff).ToList();
        var afterSmells = (after ?? []).Where(SmellAggregation.CountsForDiff).ToList();

        var beforeCounts = BuildSmellMultiset(beforeSmells);
        var afterCounts = BuildSmellMultiset(afterSmells);

        var changes = new List<SmellChange>();

        foreach (var s in beforeSmells)
        {
            var key = SmellKey(s);
            if (afterCounts.TryGetValue(key, out var remaining) && remaining > 0)
            {
                afterCounts[key] = remaining - 1;
                continue;
            }
            changes.Add(new SmellChange(s, IsResolved: true));
        }

        var beforeRemaining = BuildSmellMultiset(beforeSmells);
        foreach (var s in afterSmells)
        {
            var key = SmellKey(s);
            if (beforeRemaining.TryGetValue(key, out var remaining) && remaining > 0)
            {
                beforeRemaining[key] = remaining - 1;
                continue;
            }
            changes.Add(new SmellChange(s, IsResolved: false));
        }

        return changes.Count > 0 ? changes : null;
    }

    static Dictionary<string, int> BuildSmellMultiset(IReadOnlyList<CodeSmell> smells)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in smells)
        {
            var key = SmellKey(s);
            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;
        }
        return counts;
    }

    static string SmellKey(CodeSmell s)
    {
        var memberKey = s.MemberId ?? s.MethodName ?? "";
        return $"{s.Kind}:{memberKey}";
    }

    static IReadOnlyList<CodeSmell> FilterActiveSmells(IReadOnlyList<CodeSmell>? smells)
        => smells?.Where(s => s.Suppressed != true).ToList() ?? [];

    static readonly HashSet<string> HigherIsBetter = ["CodeHealth", "AverageMaintainabilityIndex", "MinMaintainabilityIndex"];

    static ChangeStatus ClassifyType(
        IReadOnlyList<MetricDelta<double>> doubleDeltas,
        IReadOnlyList<MetricDelta<int>> intDeltas,
        IReadOnlyList<SmellChange>? smellChanges)
    {
        var hasImproved = false;
        var hasDegraded = false;

        ClassifyDoubleDeltas(doubleDeltas, ref hasImproved, ref hasDegraded);
        ClassifyIntDeltas(intDeltas, ref hasImproved, ref hasDegraded);
        ClassifySmellChanges(smellChanges, ref hasImproved, ref hasDegraded);

        if (hasDegraded) return ChangeStatus.Degraded;
        if (hasImproved) return ChangeStatus.Improved;
        return ChangeStatus.Unchanged;
    }

    static void ClassifyDoubleDeltas(IReadOnlyList<MetricDelta<double>> deltas,
        ref bool hasImproved, ref bool hasDegraded)
    {
        foreach (var d in deltas)
        {
            if (Math.Abs(d.Delta) < 0.0001) continue;
            var improved = HigherIsBetter.Contains(d.Name) ? d.Delta > 0 : d.Delta < 0;
            if (improved) hasImproved = true;
            else hasDegraded = true;
        }
    }

    static void ClassifyIntDeltas(IReadOnlyList<MetricDelta<int>> deltas,
        ref bool hasImproved, ref bool hasDegraded)
    {
        foreach (var d in deltas)
        {
            if (d.Delta == 0) continue;
            if (d.Delta < 0) hasImproved = true;
            else hasDegraded = true;
        }
    }

    static void ClassifySmellChanges(IReadOnlyList<SmellChange>? smellChanges,
        ref bool hasImproved, ref bool hasDegraded)
    {
        if (smellChanges is null) return;
        foreach (var sc in smellChanges)
        {
            if (sc.IsResolved) hasImproved = true;
            else hasDegraded = true;
        }
    }

    static ChangeStatus ClassifyMethodDeltas(IReadOnlyList<MetricDelta<int>> deltas)
    {
        var hasImproved = false;
        var hasDegraded = false;

        foreach (var d in deltas)
        {
            if (d.Delta == 0) continue;
            // All method metrics are lower-is-better
            if (d.Delta < 0) hasImproved = true;
            else hasDegraded = true;
        }

        if (hasDegraded) return ChangeStatus.Degraded;
        if (hasImproved) return ChangeStatus.Improved;
        return ChangeStatus.Unchanged;
    }

    static TypeDiff BuildOneSidedTypeDiff(TypeMetrics metrics, ChangeStatus status)
    {
        var key = BuildDisplayTypeKey(metrics);
        return new TypeDiff(
            key, metrics.TypeName, metrics.Namespace, metrics.Assembly,
            status, [], [], [], null);
    }

    static string BuildDisplayTypeKey(TypeMetrics metrics)
    {
        var qualifiedName = TypeIdentity.GetQualifiedName(metrics);
        return string.IsNullOrEmpty(qualifiedName) ? metrics.TypeName : qualifiedName;
    }
}
