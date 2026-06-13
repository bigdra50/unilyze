using System.Text.RegularExpressions;
using Unilyze;

namespace Unilyze.Tests.Cli;

public sealed class ToolVersionInfoTests
{
    [Fact]
    public void Current_DoesNotReturnZeroVersion()
    {
        var version = ToolVersionInfo.Current;
        Assert.NotEqual("0.0.0", version);
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+"), version);
    }
}
