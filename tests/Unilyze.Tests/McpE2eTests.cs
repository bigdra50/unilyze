using System.Diagnostics;
using System.Text.Json;

namespace Unilyze.Tests;

public sealed class McpE2eTests
{
    static readonly string GoldenFixturePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "golden", "expected.json"));

    [Fact]
    public void BogusOption_ExitsOne()
    {
        var (exitCode, _, _) = CliE2eTestsHelper.Run("mcp", "--bogus");
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Help_ListsMcpSubcommand()
    {
        var (exitCode, stdout, _) = CliE2eTestsHelper.Run("--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("mcp", stdout);
    }

    [Fact]
    public void McpHelp_ListsTools()
    {
        var (exitCode, stdout, _) = CliE2eTestsHelper.Run("mcp", "--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("get_summary", stdout);
        Assert.Contains("worst_types", stdout);
    }

    [Fact]
    public void JsonRpc_InitializeToolsListAndGetSummary()
    {
        var getSummaryCall = "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"get_summary\",\"arguments\":{\"input\":\"" +
            GoldenFixturePath.Replace("\\", "\\\\") + "\"}}}";
        var stdin = string.Join('\n',
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"0.0.0"}}}""",
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            getSummaryCall);

        var (exitCode, stdout, stderr) = CliE2eTestsHelper.RunWithInput(stdin, "mcp");
        Assert.Equal(0, exitCode);

        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.All(lines, line => JsonDocument.Parse(line));

        var init = JsonDocument.Parse(lines[0]).RootElement;
        Assert.Equal("unilyze", init.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());

        var toolsList = JsonDocument.Parse(lines[1]).RootElement;
        var toolNames = toolsList.GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(McpToolSchemas.ToolNames.OrderBy(n => n, StringComparer.Ordinal).ToList(), toolNames);

        var getSummary = JsonDocument.Parse(lines[2]).RootElement;
        var summaryText = getSummary.GetProperty("result").GetProperty("content")[0]
            .GetProperty("text").GetString();
        Assert.NotNull(summaryText);
        Assert.Contains("metricsVersion:", summaryText);
        Assert.False(getSummary.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.DoesNotContain("typeMetrics", summaryText);
    }
}
