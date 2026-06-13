using System.Net;
using System.Text.Json;

namespace Unilyze.Output;

internal static class HtmlFormatter
{
    public static string Generate(
        string analysisJson,
        string projectPath,
        string? editorCommand = null) =>
        Render(analysisJson, diffJson: "null", projectPath, editorCommand);

    public static string GenerateWithDiff(
        string analysisJson,
        string diffJson,
        string projectPath,
        string? editorCommand = null) =>
        Render(analysisJson, diffJson, projectPath, editorCommand);

    static string Render(
        string analysisJson,
        string diffJson,
        string projectPath,
        string? editorCommand)
    {
        var title = Path.GetFileName(projectPath.TrimEnd('/').TrimEnd('\\'));
        if (string.IsNullOrEmpty(title)) title = "Unity Project";
        var safeAnalysisJson = EscapeInlineScriptPayload(analysisJson);
        var safeDiffJson = EscapeInlineScriptPayload(diffJson);
        var safeEditorCommand = editorCommand is null
            ? "null"
            : EscapeInlineScriptPayload(JsonSerializer.Serialize(editorCommand));

        return HtmlTemplate.Value
            .Replace("__VENDOR_SCRIPTS__", HtmlTemplate.VendorScripts)
            .Replace("__DATA_PLACEHOLDER__", safeAnalysisJson)
            .Replace("__DIFF_DATA_PLACEHOLDER__", safeDiffJson)
            .Replace("__EDITOR_COMMAND__", safeEditorCommand)
            .Replace("__TITLE__", WebUtility.HtmlEncode(title));
    }

    static string EscapeInlineScriptPayload(string json) =>
        json.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
}
