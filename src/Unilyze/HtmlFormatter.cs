using System.Net;

namespace Unilyze;

public static class HtmlFormatter
{
    public static string Generate(string analysisJson, string projectPath) =>
        Render(analysisJson, diffJson: "null", projectPath);

    public static string GenerateWithDiff(string analysisJson, string diffJson, string projectPath) =>
        Render(analysisJson, diffJson, projectPath);

    static string Render(string analysisJson, string diffJson, string projectPath)
    {
        var title = Path.GetFileName(projectPath.TrimEnd('/').TrimEnd('\\'));
        if (string.IsNullOrEmpty(title)) title = "Unity Project";

        return HtmlTemplate.Value
            .Replace("__VENDOR_SCRIPTS__", HtmlTemplate.VendorScripts)
            .Replace("__DATA_PLACEHOLDER__", analysisJson)
            .Replace("__DIFF_DATA_PLACEHOLDER__", diffJson)
            .Replace("__TITLE__", WebUtility.HtmlEncode(title));
    }
}
