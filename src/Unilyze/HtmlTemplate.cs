namespace Unilyze;

internal static class HtmlTemplate
{
    // The viewer markup lives in Templates/viewer.html (embedded resource) so it can be
    // edited with HTML tooling instead of inside a 2,400-line C# string literal.
    internal static string Value { get; } = LoadEmbedded("Unilyze.Templates.viewer.html");

    static string LoadEmbedded(string resourceName)
    {
        using var stream = typeof(HtmlTemplate).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
