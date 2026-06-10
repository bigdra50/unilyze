using System.Text.Json;

namespace Unilyze;

internal static class MetricsRunner
{
    public static int Run(string[] args)
    {
        var usageError = ProgramHelpers.ValidateMetricsArgs(args);
        if (usageError != 0)
            return usageError;
        return PrintMetrics();
    }

    static int PrintMetrics()
    {
        var text = EmbeddedCliText.Metrics
            .Replace("{{SMELL_THRESHOLDS}}", SmellThresholds.FormatMetricsCliHelp());
        Console.Write(text);
        return 0;
    }
}
