namespace Unilyze.Tests;

public sealed class ThirdPartyCloneFilterTests
{
    static CloneClass MakeClass(params (string File, int Start, int End)[] occurrences) =>
        new(1, 100, occurrences.Select(o => new CloneOccurrence(o.File, o.Start, o.End)).ToList());

    [Fact]
    public void Apply_SameThirdPartyPair_Suppressed()
    {
        var roots = ThirdPartyCloneFilter.ResolveRoots("/proj", ["Assets/Plugins"]);
        var clone = MakeClass(
            ("/proj/Assets/Plugins/A.cs", 1, 10),
            ("/proj/Assets/Plugins/B.cs", 1, 10));

        var (classes, suppressed) = ThirdPartyCloneFilter.Apply([clone], roots, includeThirdParty: false);

        Assert.Empty(classes);
        Assert.Equal(1, suppressed);
    }

    [Fact]
    public void Apply_CrossBoundaryPair_Kept()
    {
        var roots = ThirdPartyCloneFilter.ResolveRoots("/proj", ["Assets/Plugins"]);
        var clone = MakeClass(
            ("/proj/Assets/Plugins/A.cs", 1, 10),
            ("/proj/Assets/Scripts/B.cs", 1, 10));

        var (classes, suppressed) = ThirdPartyCloneFilter.Apply([clone], roots, includeThirdParty: false);

        Assert.Single(classes);
        Assert.Equal(0, suppressed);
    }

    [Fact]
    public void Apply_IncludeThirdParty_RevealsSuppressed()
    {
        var roots = ThirdPartyCloneFilter.ResolveRoots("/proj", ["Assets/Plugins"]);
        var clone = MakeClass(
            ("/proj/Assets/Plugins/A.cs", 1, 10),
            ("/proj/Assets/Plugins/B.cs", 1, 10));

        var (classes, suppressed) = ThirdPartyCloneFilter.Apply([clone], roots, includeThirdParty: true);

        Assert.Single(classes);
        Assert.Equal(0, suppressed);
    }
}
