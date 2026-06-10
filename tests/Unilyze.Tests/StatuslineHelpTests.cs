namespace Unilyze.Tests;

public sealed class StatuslineHelpTests
{
    [Fact]
    public void BuildUsageText_DocumentsEveryFormatterToken()
    {
        var help = StatuslineRunner.BuildUsageText();

        Assert.Contains("CH", help);
        Assert.Contains("MI", help);
        Assert.Contains("smells", help);
        Assert.Contains("\U0001f534", help); // 🔴
        Assert.Contains("\U0001f4e6", help); // 📦
        Assert.Contains("\u267b", help);     // ♻
        Assert.Contains("[syntax]", help);
        Assert.Contains("[core]", help);
        Assert.Contains("[full]", help);
    }
}
