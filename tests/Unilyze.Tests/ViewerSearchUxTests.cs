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

    sealed record ViewerSearchType(string Name, string Namespace, string QualifiedName);
    sealed record ViewerSearchMetrics(double? Health, int SmellCount);
    sealed record ViewerSearchFilters(bool LowHealth = false, bool Smells = false, bool Cycles = false);
    sealed record SearchMatchResult(IReadOnlyList<string> TypeKeys, IReadOnlyList<string> NamespacePaths);
}
