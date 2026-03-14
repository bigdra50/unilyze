namespace Unilyze;

public static class DiffCalculator
{
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

        return new DiffResult(
            before.ProjectPath, after.ProjectPath,
            before.AnalyzedAt, after.AnalyzedAt,
            summary,
            improved, degraded, unchanged, added, removed);
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
                var deltas = new List<MetricDelta<int>>
                {
                    new("CognitiveComplexity", b.CognitiveComplexity, a.CognitiveComplexity, a.CognitiveComplexity - b.CognitiveComplexity),
                    new("CyclomaticComplexity", b.CyclomaticComplexity, a.CyclomaticComplexity, a.CyclomaticComplexity - b.CyclomaticComplexity),
                    new("MaxNestingDepth", b.MaxNestingDepth, a.MaxNestingDepth, a.MaxNestingDepth - b.MaxNestingDepth),
                    new("LineCount", b.LineCount, a.LineCount, a.LineCount - b.LineCount),
                };
                var status = ClassifyMethodDeltas(deltas);
                diffs.Add(new MethodDiff(a.MethodName, a.ParameterCount, status, deltas));
            }
        }

        return diffs;
    }

    static string MethodKey(MethodMetrics m) => $"{m.MethodName}:{m.ParameterCount}";

    static IReadOnlyList<SmellChange>? ComputeSmellChanges(
        IReadOnlyList<CodeSmell>? before, IReadOnlyList<CodeSmell>? after)
    {
        if (before is null && after is null) return null;

        var beforeSmells = before ?? [];
        var afterSmells = after ?? [];

        var beforeKeys = new HashSet<string>(beforeSmells.Select(SmellKey));
        var afterKeys = new HashSet<string>(afterSmells.Select(SmellKey));

        var changes = new List<SmellChange>();

        foreach (var s in beforeSmells)
        {
            if (!afterKeys.Contains(SmellKey(s)))
                changes.Add(new SmellChange(s, IsResolved: true));
        }

        foreach (var s in afterSmells)
        {
            if (!beforeKeys.Contains(SmellKey(s)))
                changes.Add(new SmellChange(s, IsResolved: false));
        }

        return changes.Count > 0 ? changes : null;
    }

    static string SmellKey(CodeSmell s) => $"{s.Kind}:{s.MethodName ?? ""}";

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
