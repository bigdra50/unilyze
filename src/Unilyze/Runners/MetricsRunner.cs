using Unilyze.Config;
using Unilyze.Cli;
using System.Text.Json;

namespace Unilyze.Runners;

internal static class MetricsRunner
{
    public static int Run(string[] args)
    {
        var usageError = CliArgValidation.ValidateMetricsArgs(args);
        if (usageError != 0)
            return usageError;

        var opts = ProgramHelpers.ParseOptions(args);
        var profile = opts.GetValueOrDefault("--profile");
        return PrintMetrics(profile);
    }

    static int PrintMetrics(string? profile)
    {
        var text = EmbeddedCliText.Metrics
            .Replace("{{SMELL_THRESHOLDS}}", SmellThresholdProfiles.FormatMetricsCliHelp(profile));
        Console.Write(text);
        return 0;
    }
}
