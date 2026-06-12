using System.Text;
using System.Text.Json;

namespace Unilyze;

internal static class CalibrateRunner
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || CliArgValidationSupport.IsHelpRequest(args))
            return PrintUsage();

        var usageError = CliArgValidation.ValidateCalibrateArgs(args);
        if (usageError != 0)
            return usageError;

        var positional = args.Where(a => !a.StartsWith('-')).ToList();
        if (positional.Count < 1)
        {
            Console.Error.WriteLine("Usage: unilyze calibrate <dir-of-jsons> [-o thresholds.json]");
            return 1;
        }

        var opts = ProgramHelpers.ParseOptions(args);
        var output = opts.GetValueOrDefault("-o") ?? opts.GetValueOrDefault("--output");
        var dir = positional[0];

        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"Directory not found: {dir}");
            return 1;
        }

        try
        {
            var jsonFiles = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f)
                .ToList();

            if (jsonFiles.Count < 2)
            {
                Console.Error.WriteLine(
                    $"At least two JSON snapshots are required; found {jsonFiles.Count} in: {dir}");
                return 1;
            }

            var snapshots = new List<(string FileName, AnalysisResult Result)>();
            foreach (var file in jsonFiles)
            {
                var json = File.ReadAllText(file);
                var result = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.AnalysisResult);
                if (result is null)
                {
                    Console.Error.WriteLine($"Skipping invalid file: {file}");
                    continue;
                }

                snapshots.Add((Path.GetFileName(file), result));
            }

            if (snapshots.Count < 2)
            {
                Console.Error.WriteLine("At least two valid analysis snapshots are required.");
                return 1;
            }

            var calibrated = ThresholdCalibrator.Calibrate(snapshots);
            var calibratedJson = JsonSerializer.Serialize(calibrated, CalibrateJsonContext.Default.CalibrateResult);

            PrintSummary(calibrated);

            return ProgramHelpers.WriteOutput(calibratedJson, output);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static void PrintSummary(CalibrateResult result)
    {
        Console.Error.WriteLine($"Calibration: {result.Sources.Count} snapshot(s), metricsVersion {result.MetricsVersion}");
        Console.Error.WriteLine(result.Methodology);
        Console.Error.WriteLine();
        Console.Error.WriteLine("  Source                     Methods  Types  Method LOC");
        Console.Error.WriteLine("  -------------------------  -------  -----  ----------");
        foreach (var source in result.Sources)
        {
            Console.Error.WriteLine(
                $"  {Truncate(source.FileName, 25),-25}  {source.MethodCount,7}  {source.TypeCount,5}  {source.TotalMethodLoc,10}");
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine(FormatThresholdTable(result.Metrics));
    }

    static string FormatThresholdTable(CalibrateMetricsBlock metrics)
    {
        var sb = new StringBuilder();
        sb.AppendLine("  Metric                 P-low   P-mod   P-high  (risk band upper bounds)");
        sb.AppendLine("  ---------------------  ------  ------  ------");
        AppendMetricRow(sb, "LongMethod (LOC)", metrics.MethodLines);
        AppendMetricRow(sb, "CycCC", metrics.CyclomaticComplexity);
        AppendMetricRow(sb, "CogCC", metrics.CognitiveComplexity);
        AppendMetricRow(sb, "MaxNestingDepth", metrics.MaxNestingDepth);
        AppendParameterRow(sb, "ParameterCount", metrics.ParameterCount);
        AppendMetricRow(sb, "GodClass (methods)", metrics.MethodsPerType);
        AppendMetricRow(sb, "GodClass (type LOC)", metrics.TypeLines);
        return sb.ToString().TrimEnd();
    }

    static void AppendMetricRow(StringBuilder sb, string label, CalibrateMetricThresholds metric)
    {
        var bands = metric.RiskBands;
        sb.AppendLine(
            $"  {label,-21}  {bands.LowUpper,6}  {bands.ModerateUpper,6}  {bands.HighUpper,6}");
    }

    static void AppendParameterRow(StringBuilder sb, string label, CalibrateParameterThresholds metric)
    {
        var bands = metric.RiskBands;
        sb.AppendLine(
            $"  {label,-21}  {bands.LowUpper,6}  {bands.ModerateUpper,6}  {bands.HighUpper,6}");
    }

    static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";

    static int PrintUsage()
    {
        Console.WriteLine("""
            unilyze calibrate - Derive smell-threshold candidates from analysis snapshots

            Usage:
              unilyze calibrate <dir-of-jsons>              Output calibration JSON to stdout
              unilyze calibrate <dir-of-jsons> -o out.json  Save calibration report to file

            Input:
              Two or more unilyze JSON snapshots (one per system) with matching metricsVersion.
              Method-level metrics are LOC-weighted within each system and pooled with equal
              per-system weight (Alves, Ypma & Visser, ICSM 2010).

            Output:
              JSON report with 70/80/90 percentile thresholds (80/90/95 for parameter count),
              four risk categories (low / moderate / high / veryHigh), provenance metadata,
              and a ready-to-apply .unilyze.json smells fragment.

            Options:
              -o, --output    Output file path
              -h, --help      Show this help

            Exit codes:
              0  Success
              1  Usage error or incompatible snapshots
            """);
        return 0;
    }
}
