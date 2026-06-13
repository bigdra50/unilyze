namespace Unilyze.Cli;

internal static class EmbeddedCliText
{
    internal static string Metrics { get; } = LoadEmbedded("Unilyze.Resources.metrics.txt");
    internal static string Schema { get; } = LoadEmbedded("Unilyze.Resources.schema.txt");

    static string LoadEmbedded(string resourceName)
    {
        using var stream = typeof(EmbeddedCliText).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
