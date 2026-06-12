using System.Text.Json;

namespace Unilyze.Tests;

public sealed class ViewerSearchUxTests
{
    [Fact]
    public void Generate_EmbedsSearchUxEnhancements()
    {
        var html = GenerateSampleHtml();

        Assert.Contains("SEARCH_EXPAND_CAP", html);
        Assert.Contains("expandNamespaceAncestors", html);
        Assert.Contains("collectSearchMatches", html);
        Assert.Contains("installViewerKeyboard", html);
        Assert.Contains("filter-chip", html);
        Assert.Contains("Search types...  ( / )", html);
        Assert.Contains("id=\"fLowHealth\"", html);
        Assert.Contains("id=\"fSmells\"", html);
        Assert.Contains("id=\"fCycles\"", html);
        Assert.Contains("body.offline-mode .filter-chip", html);
    }

    [Theory]
    [InlineData("A.B.C", new[] { "A", "A.B", "A.B.C" })]
    [InlineData("(global)", new[] { "(global)" })]
    [InlineData("Sample", new[] { "Sample" })]
    public void ExpandNamespaceAncestors_AddsAllPrefixPaths(string ns, string[] expected)
    {
        var expanded = new HashSet<string>();
        ExpandNamespaceAncestors(ns, expanded);
        Assert.Equal(expected.OrderBy(x => x), expanded.OrderBy(x => x));
    }

    [Fact]
    public void CollectSearchMatches_FindsNestedTypeRegardlessOfVisibility()
    {
        var types = new Dictionary<string, ViewerSearchType>
        {
            ["Asm::Outer.Inner.Target"] = new("Target", "Outer.Inner", "Asm::Outer.Inner.Target"),
            ["Asm::Outer.Other"] = new("Other", "Outer", "Asm::Outer.Other")
        };

        var matches = CollectSearchMatches(
            query: "target",
            types,
            metrics: new Dictionary<string, ViewerSearchMetrics>(),
            cycleTypes: new HashSet<string>(),
            filters: new ViewerSearchFilters());

        Assert.Single(matches.TypeKeys);
        Assert.Equal("Asm::Outer.Inner.Target", matches.TypeKeys[0]);
    }

    [Fact]
    public void CollectSearchMatches_AppliesQuickFiltersWithNullHealthHandling()
    {
        var types = new Dictionary<string, ViewerSearchType>
        {
            ["A::Low"] = new("Low", "Ns", "A::Low"),
            ["A::NullHealth"] = new("NullHealth", "Ns", "A::NullHealth"),
            ["A::Smelly"] = new("Smelly", "Ns", "A::Smelly")
        };
        var metrics = new Dictionary<string, ViewerSearchMetrics>
        {
            ["A::Low"] = new(6.5, 0),
            ["A::NullHealth"] = new(null, 0),
            ["A::Smelly"] = new(9.0, 2)
        };

        var lowHealth = CollectSearchMatches("", types, metrics, new HashSet<string>(), new ViewerSearchFilters(LowHealth: true));
        Assert.Equal(["A::Low"], lowHealth.TypeKeys);

        var smells = CollectSearchMatches("", types, metrics, new HashSet<string>(), new ViewerSearchFilters(Smells: true));
        Assert.Equal(["A::Smelly"], smells.TypeKeys);
    }

    [Fact]
    public void SearchExpansion_RespectsCap()
    {
        Assert.True(51 > SearchExpandCap);
        Assert.False(50 > SearchExpandCap);
    }

    [Fact]
    public void LazyElements_Synthetic1500Types_ReducesInitialElementCount()
    {
        var graph = CreateSyntheticGraph(typeCount: 1500, dependencyCount: 4500, namespaceCount: 60);

        var eagerCount = CountEagerElements(graph);
        var lazyInitialCount = CountLazyInitialElements(graph);

        Assert.Equal((6122, 122), (eagerCount, lazyInitialCount));
    }

    [Fact]
    public void AggregateMetaEdges_RoutesCollapsedEndpointsWithoutDoubleCountingVisibleEdges()
    {
        var dependencies = new[]
        {
            new ViewerDependency("A::Root.Visible", "A::Child.Hidden"),
            new ViewerDependency("A::Child.Hidden", "A::Other.Hidden"),
            new ViewerDependency("A::Root.Visible", "A::Root.OtherVisible")
        };
        var owners = new Dictionary<string, string>
        {
            ["A::Root.Visible"] = "Root",
            ["A::Root.OtherVisible"] = "Root",
            ["A::Child.Hidden"] = "Root.Child",
            ["A::Other.Hidden"] = "Other"
        };
        var visibleTypes = new HashSet<string> { "A::Root.Visible", "A::Root.OtherVisible" };
        var visibleAncestors = new Dictionary<string, string?>
        {
            ["Root.Child"] = "ns:Root.Child",
            ["Other"] = "ns:Other"
        };

        var actual = AggregateMetaEdges(dependencies, owners, visibleTypes, visibleAncestors);

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["t:A::Root.Visible>ns:Root.Child"] = 1,
                ["ns:Root.Child>ns:Other"] = 1
            },
            actual);
    }

    static string GenerateSampleHtml()
    {
        var type = new TypeNodeInfo(
            "Target", "Outer.Inner", "class", [], null, [], [], [], [], [], null,
            "Sample.Asm", "Target.cs", false, 10, 1,
            "Outer.Inner.Target", "Sample.Asm::Outer.Inner.Target");
        var result = new AnalysisResult(
            "/tmp/SampleProject",
            DateTimeOffset.UtcNow,
            [],
            [type],
            []);
        var json = JsonSerializer.Serialize(result, AnalysisJsonContext.Default.AnalysisResult);
        return HtmlFormatter.Generate(json, result.ProjectPath);
    }

    const int SearchExpandCap = 50;

    static void ExpandNamespaceAncestors(string ns, ISet<string> expanded)
    {
        var parts = ns.Split('.');
        for (var i = 1; i <= parts.Length; i++)
            expanded.Add(string.Join('.', parts.Take(i)));
    }

    static SearchMatchResult CollectSearchMatches(
        string query,
        IReadOnlyDictionary<string, ViewerSearchType> types,
        IReadOnlyDictionary<string, ViewerSearchMetrics> metrics,
        ISet<string> cycleTypes,
        ViewerSearchFilters filters)
    {
        var q = query.Trim().ToLowerInvariant();
        var typeKeys = new List<string>();
        if (!HasActiveSearch(q, filters))
            return new SearchMatchResult(typeKeys, []);

        foreach (var (key, type) in types)
        {
            if (!PassesQuickFilters(key, metrics, cycleTypes, filters))
                continue;
            if (!string.IsNullOrEmpty(q) && !TypeMatchesText(type, q))
                continue;
            typeKeys.Add(key);
        }

        return new SearchMatchResult(typeKeys, []);
    }

    static bool HasActiveSearch(string query, ViewerSearchFilters filters) =>
        !string.IsNullOrEmpty(query) || filters.LowHealth || filters.Smells || filters.Cycles;

    static bool PassesQuickFilters(
        string typeKey,
        IReadOnlyDictionary<string, ViewerSearchMetrics> metrics,
        ISet<string> cycleTypes,
        ViewerSearchFilters filters)
    {
        metrics.TryGetValue(typeKey, out var metric);
        metric ??= new ViewerSearchMetrics(null, 0);

        if (filters.LowHealth && (metric.Health is null || metric.Health >= 7))
            return false;
        if (filters.Smells && metric.SmellCount <= 0)
            return false;
        if (filters.Cycles && !cycleTypes.Contains(typeKey))
            return false;
        return true;
    }

    static bool TypeMatchesText(ViewerSearchType type, string query)
    {
        if (type.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;
        return type.QualifiedName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    static ViewerGraph CreateSyntheticGraph(int typeCount, int dependencyCount, int namespaceCount)
    {
        var namespaces = Enumerable.Range(0, namespaceCount)
            .Select(index => $"Synthetic.Ns{index:00}")
            .Prepend("Synthetic")
            .ToArray();
        var types = Enumerable.Range(0, typeCount)
            .Select(index => new ViewerGraphType(
                $"Synthetic.Assembly::Synthetic.Ns{index % namespaceCount:00}.Type{index}",
                $"Synthetic.Ns{index % namespaceCount:00}"))
            .ToArray();
        var dependencies = Enumerable.Range(0, dependencyCount)
            .Select(index => new ViewerDependency(
                types[index % typeCount].Id,
                types[(index * 37 + 17) % typeCount].Id))
            .ToArray();
        return new ViewerGraph(namespaces, types, dependencies, "Synthetic");
    }

    static int CountEagerElements(ViewerGraph graph) =>
        graph.Namespaces.Count * 2 + graph.Types.Count + graph.Dependencies.Count;

    static int CountLazyInitialElements(ViewerGraph graph)
    {
        var initialTypes = graph.Types
            .Where(type => type.Namespace == graph.InitiallyExpandedNamespace)
            .Select(type => type.Id)
            .ToHashSet();
        var initialEdges = graph.Dependencies.Count(dependency =>
            initialTypes.Contains(dependency.FromId) && initialTypes.Contains(dependency.ToId));
        return graph.Namespaces.Count * 2 + initialTypes.Count + initialEdges;
    }

    static IReadOnlyDictionary<string, int> AggregateMetaEdges(
        IReadOnlyList<ViewerDependency> dependencies,
        IReadOnlyDictionary<string, string> owners,
        ISet<string> visibleTypes,
        IReadOnlyDictionary<string, string?> visibleAncestors)
    {
        var result = new Dictionary<string, int>();
        foreach (var dependency in dependencies)
        {
            if (visibleTypes.Contains(dependency.FromId) && visibleTypes.Contains(dependency.ToId))
                continue;
            var source = visibleTypes.Contains(dependency.FromId)
                ? $"t:{dependency.FromId}"
                : visibleAncestors.GetValueOrDefault(owners[dependency.FromId]);
            var target = visibleTypes.Contains(dependency.ToId)
                ? $"t:{dependency.ToId}"
                : visibleAncestors.GetValueOrDefault(owners[dependency.ToId]);
            if (source is null || target is null || source == target)
                continue;
            var key = $"{source}>{target}";
            result[key] = result.GetValueOrDefault(key) + 1;
        }
        return result;
    }

    sealed record ViewerSearchType(string Name, string Namespace, string QualifiedName);
    sealed record ViewerSearchMetrics(double? Health, int SmellCount);
    sealed record ViewerSearchFilters(bool LowHealth = false, bool Smells = false, bool Cycles = false);
    sealed record SearchMatchResult(IReadOnlyList<string> TypeKeys, IReadOnlyList<string> NamespacePaths);
    sealed record ViewerGraph(
        IReadOnlyList<string> Namespaces,
        IReadOnlyList<ViewerGraphType> Types,
        IReadOnlyList<ViewerDependency> Dependencies,
        string InitiallyExpandedNamespace);
    sealed record ViewerGraphType(string Id, string Namespace);
    sealed record ViewerDependency(string FromId, string ToId);
}
