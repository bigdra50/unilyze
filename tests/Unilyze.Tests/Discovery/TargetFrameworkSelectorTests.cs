namespace Unilyze.Tests.Discovery;

public sealed class TargetFrameworkSelectorTests
{
    [Fact]
    public void Select_ExplicitTfm_WinsOverCsprojAndRuntime()
    {
        var selected = TargetFrameworkSelector.Select(
            ["net8.0", "net10.0"],
            ["net10.0"],
            "net8.0",
            "net10.0");

        Assert.Equal("net8.0", selected);
    }

    [Fact]
    public void Select_UsesFirstCsprojTfmWhenExplicitMissing()
    {
        var selected = TargetFrameworkSelector.Select(
            ["net8.0", "net10.0"],
            ["net10.0", "net8.0"],
            null,
            "net8.0");

        Assert.Equal("net10.0", selected);
    }

    [Fact]
    public void Select_PicksHighestWhenNoHintMatches()
    {
        var selected = TargetFrameworkSelector.Select(
            ["net8.0", "net10.0"],
            [],
            null,
            null);

        Assert.Equal("net10.0", selected);
    }
}
