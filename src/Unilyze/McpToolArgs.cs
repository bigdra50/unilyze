using System.Text.Json;

namespace Unilyze;

internal sealed class McpToolArgs
{
    readonly JsonElement _root;

    McpToolArgs(JsonElement root) => _root = root;

    public static McpToolArgs From(JsonElement? arguments) =>
        arguments is { ValueKind: JsonValueKind.Object } el ? new McpToolArgs(el) : new McpToolArgs(default);

    public string? GetString(string name)
    {
        if (_root.ValueKind != JsonValueKind.Object || !_root.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    public bool TryGetInt(string name, out int result)
    {
        result = 0;
        if (_root.ValueKind != JsonValueKind.Object || !_root.TryGetProperty(name, out var value))
            return false;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result))
            return true;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out result);
    }

    public string PathOrDefault() => GetString("path") ?? ".";

    public string? Input => GetString("input");
}
