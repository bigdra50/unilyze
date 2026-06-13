namespace Unilyze.Tests;

public sealed class StatuslineHelpTests
{
    [Fact]
    public void BuildUsageText_DocumentsEveryFormatterToken()
    {
        var help = StatuslineRunner.BuildUsageText();

        Assert.Contains("CH", help);
        Assert.Contains("W:", help);
        Assert.Contains("T:", help);
        Assert.Contains("MI", help);
        Assert.Contains("smells", help);
        Assert.Contains("\U0001f534", help); // 🔴
        Assert.Contains("\U0001f4e6", help); // 📦
        Assert.Contains("\u267b", help);     // ♻
        Assert.Contains("[syntax]", help);
        Assert.Contains("[core]", help);
        Assert.Contains("[full]", help);
        Assert.Contains("--verbose", help);
        Assert.Contains("--quiet", help);
        Assert.Contains("--background-refresh", help);
        Assert.Contains("--incremental", help);
        Assert.Contains("--codehealth-v1", help);
        Assert.Contains("--show-mi", help);
        Assert.Contains("With --show-mi:", help);
        Assert.DoesNotContain("T:7.8 MI:", help);
        Assert.DoesNotContain("MI:<n>         = Average Maintainability Index (integer), always shown", help);
        Assert.Contains(".unilyze/cache", help);
    }
}
