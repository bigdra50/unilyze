using System.Text.Json;
using System.Text.Json.Nodes;

namespace Unilyze;

internal static class McpJsonRpc
{
    public const string ProtocolVersion = "2024-11-05";

    public static void WriteResponse(TextWriter stdout, JsonNode? id, JsonNode result)
    {
        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result,
        };
        WriteFrame(stdout, payload);
    }

    public static void WriteError(TextWriter stdout, JsonNode? id, int code, string message)
    {
        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        };
        WriteFrame(stdout, payload);
    }

    public static JsonObject BuildInitializeResult()
    {
        return new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject(),
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "unilyze",
                ["version"] = ToolVersionInfo.Current,
            },
        };
    }

    public static JsonObject BuildToolCallResult(McpToolResult toolResult) =>
        new()
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = toolResult.Text,
                },
            },
            ["isError"] = toolResult.IsError,
        };

    static void WriteFrame(TextWriter stdout, JsonObject payload)
    {
        stdout.WriteLine(payload.ToJsonString());
        stdout.Flush();
    }
}
