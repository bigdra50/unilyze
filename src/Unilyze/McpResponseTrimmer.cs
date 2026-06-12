namespace Unilyze;

internal static class McpResponseTrimmer
{
    public const int DefaultMaxChars = 16_000;

    public static int ResolveMaxChars(McpToolArgs args) =>
        args.TryGetInt("max_chars", out var max) && max > 0 ? max : DefaultMaxChars;

    public static string Apply(string text, int maxChars)
    {
        if (text.Length <= maxChars)
            return text;

        const string suffix = "\n\n… [truncated to max_chars]";
        var keep = Math.Max(0, maxChars - suffix.Length);
        return text[..keep] + suffix;
    }
}
