namespace Unilyze;

internal static class McpRunner
{
    public static int Run(string[] args)
    {
        if (ProgramHelpers.IsHelpRequest(args))
            return PrintUsage();

        var usageError = ProgramHelpers.ValidateMcpArgs(args);
        if (usageError != 0)
            return usageError;

        try
        {
            McpStdioServer.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static int PrintUsage()
    {
        Console.WriteLine("""
            unilyze mcp - MCP server over stdio for agent integration

            Usage:
              unilyze mcp

            Exposes MCP tools (JSON-RPC over stdin/stdout):
              analyze          Compact analysis summary (types, CodeHealth, smells, versions)
              get_summary      Same summary from cached or loaded snapshot
              worst_types      Lowest CodeHealth types as Markdown evidence packs
              query_type       Single-type evidence pack (Markdown or JSON)
              diff             Changed-only Markdown delta between snapshots
              hotspot          Top-N git churn × complexity hotspots
              baseline_status  Baseline file presence and fingerprint counts
              triage_add       Add a triage verdict to .unilyze/triage.json
              schema           JSON output field reference
              version          Tool and metrics version strings

            Common tool arguments:
              path             Project root for fresh analysis (default: .)
              input            Existing analysis JSON snapshot path
              max_chars        Trim large responses (default: 16000)

            Protocol output goes to stdout; logs and progress go to stderr.
            Register with MCP clients, e.g.:
              claude mcp add unilyze -- unilyze mcp

            Options:
              -h, --help       Show this help

            Exit codes:
              0  Success (server ran until stdin closed)
              1  Usage error
            """);
        return 0;
    }
}
