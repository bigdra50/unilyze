using System.Text.Json.Nodes;

namespace Unilyze;

internal static class McpToolSchemas
{
    public static readonly string[] ToolNames =
    [
        "analyze", "get_summary", "worst_types", "query_type", "diff",
        "hotspot", "baseline_status", "triage_add", "schema", "version",
    ];

    public static JsonObject BuildToolsList()
    {
        var tools = new JsonArray();
        foreach (var name in ToolNames)
            tools.Add(BuildTool(name));
        return new JsonObject { ["tools"] = tools };
    }

    static JsonObject BuildTool(string name) => new()
    {
        ["name"] = name,
        ["description"] = Descriptions[name],
        ["inputSchema"] = Schemas[name],
    };

    static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        ["analyze"] = "Run or load analysis and return a compact summary (no full typeMetrics dump).",
        ["get_summary"] = "Return the compact analysis summary from a snapshot or project path.",
        ["worst_types"] = "Lowest CodeHealth types as Markdown evidence packs (same stack as unilyze query).",
        ["query_type"] = "Single-type evidence pack; ambiguous names return candidate list.",
        ["diff"] = "Changed-only Markdown delta between before/after snapshots or base_ref.",
        ["hotspot"] = "Top-N git churn × complexity hotspots as a Markdown table.",
        ["baseline_status"] = "Report baseline file presence, fingerprint count, and suppressed smells.",
        ["triage_add"] = "Add a triage verdict to .unilyze/triage.json.",
        ["schema"] = "JSON output field reference text.",
        ["version"] = "Return toolVersion and metricsVersion.",
    };

    static readonly Dictionary<string, JsonObject> Schemas = new(StringComparer.Ordinal)
    {
        ["analyze"] = WithPathInput(new JsonObject()),
        ["get_summary"] = WithPathInput(new JsonObject()),
        ["worst_types"] = WithPathInput(new JsonObject
        {
            ["count"] = IntProperty("Number of worst types (default 5)"),
            ["format"] = StringProperty("Output format: md or json (default md)"),
        }),
        ["query_type"] = WithPathInput(new JsonObject
        {
            ["type"] = StringProperty("Simple or qualified type name"),
            ["format"] = StringProperty("Output format: md or json (default md)"),
        }),
        ["diff"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = StringProperty("Project path for base_ref analysis"),
                ["input"] = StringProperty("After snapshot JSON path (alias for after_input)"),
                ["after_input"] = StringProperty("After snapshot JSON path"),
                ["before_input"] = StringProperty("Before snapshot JSON path"),
                ["base_ref"] = StringProperty("Git ref to analyze as the before side"),
                ["max_chars"] = IntProperty("Trim response to this many characters"),
            },
        },
        ["hotspot"] = WithPathInput(new JsonObject
        {
            ["since"] = StringProperty("Git log since spec (default 12.month)"),
            ["top_n"] = IntProperty("Number of hotspots (default 20)"),
        }),
        ["baseline_status"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = StringProperty("Project root (default .)"),
                ["input"] = StringProperty("Optional snapshot for suppressedCount"),
                ["max_chars"] = IntProperty("Trim response to this many characters"),
            },
        },
        ["triage_add"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = StringProperty("Project root (default .)"),
                ["id"] = StringProperty("Finding id (required)"),
                ["verdict"] = StringProperty("confirmed, false-positive, or wontfix (required)"),
                ["reason"] = StringProperty("Optional rationale"),
                ["by"] = StringProperty("Optional author metadata"),
                ["triage_output"] = StringProperty("Override triage file path"),
                ["max_chars"] = IntProperty("Trim response to this many characters"),
            },
            ["required"] = new JsonArray { "id", "verdict" },
        },
        ["schema"] = WithMaxCharsOnly(),
        ["version"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
        },
    };

    static JsonObject WithPathInput(JsonObject properties)
    {
        properties["path"] = StringProperty("Project root for fresh analysis (default .)");
        properties["input"] = StringProperty("Existing analysis JSON snapshot path");
        properties["max_chars"] = IntProperty("Trim response to this many characters (default 16000)");
        return new JsonObject { ["type"] = "object", ["properties"] = properties };
    }

    static JsonObject WithMaxCharsOnly() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["max_chars"] = IntProperty("Trim response to this many characters"),
        },
    };

    static JsonObject StringProperty(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description,
    };

    static JsonObject IntProperty(string description) => new()
    {
        ["type"] = "integer",
        ["description"] = description,
    };
}
