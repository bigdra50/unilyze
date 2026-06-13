using System.Text.Json;
using System.Text.Json.Nodes;

namespace Unilyze.Mcp;

internal static class McpStdioServer
{
    public static void Run()
    {
        var handlers = new McpToolHandlers();
        var stdin = Console.OpenStandardInput();
        using var reader = new StreamReader(stdin);
        var stdout = Console.Out;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(line);
            }
            catch (JsonException ex)
            {
                McpJsonRpc.WriteError(stdout, id: null, -32700, $"Parse error: {ex.Message}");
                continue;
            }

            if (root is not JsonObject obj)
            {
                McpJsonRpc.WriteError(stdout, id: null, -32600, "Invalid Request");
                continue;
            }

            HandleMessage(obj, handlers, stdout);
        }
    }

    static void HandleMessage(JsonObject message, McpToolHandlers handlers, TextWriter stdout)
    {
        var id = message["id"];
        var method = message["method"]?.GetValue<string>();
        if (method is null)
        {
            McpJsonRpc.WriteError(stdout, id, -32600, "Invalid Request: missing method");
            return;
        }

        switch (method)
        {
            case "initialize":
                HandleInitialize(message, id, stdout);
                break;
            case "notifications/initialized":
                break;
            case "tools/list":
                McpJsonRpc.WriteResponse(stdout, id, McpToolSchemas.BuildToolsList());
                break;
            case "tools/call":
                HandleToolCall(message, handlers, id, stdout);
                break;
            default:
                McpJsonRpc.WriteError(stdout, id, -32601, $"Method not found: {method}");
                break;
        }
    }

    static void HandleInitialize(JsonObject message, JsonNode? id, TextWriter stdout)
    {
        if (id is null)
        {
            McpJsonRpc.WriteError(stdout, null, -32600, "Invalid Request: initialize requires id");
            return;
        }

        McpJsonRpc.WriteResponse(stdout, id, McpJsonRpc.BuildInitializeResult());
    }

    static void HandleToolCall(JsonObject message, McpToolHandlers handlers, JsonNode? id, TextWriter stdout)
    {
        if (id is null)
        {
            McpJsonRpc.WriteError(stdout, null, -32600, "Invalid Request: tools/call requires id");
            return;
        }

        var @params = message["params"] as JsonObject;
        var name = @params?["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name))
        {
            McpJsonRpc.WriteError(stdout, id, -32602, "Invalid params: missing tool name");
            return;
        }

        JsonElement? arguments = null;
        if (@params?["arguments"] is JsonObject argsObject)
            arguments = JsonDocument.Parse(argsObject.ToJsonString()).RootElement;

        var toolArgs = McpToolArgs.From(arguments);
        var result = handlers.Call(name, toolArgs);
        McpJsonRpc.WriteResponse(stdout, id, McpJsonRpc.BuildToolCallResult(result));
    }
}
