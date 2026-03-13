using System.Net;

namespace Unilyze;

public static class HtmlFormatter
{
    public static string Generate(string analysisJson, string projectPath)
    {
        var title = Path.GetFileName(projectPath.TrimEnd('/').TrimEnd('\\'));
        if (string.IsNullOrEmpty(title)) title = "Unity Project";

        return HtmlTemplate.Value
            .Replace("__DATA_PLACEHOLDER__", analysisJson)
            .Replace("__TITLE__", WebUtility.HtmlEncode(title));
    }
}
