namespace Unilyze;

internal static class ToolVersionInfo
{
    public static string Current =>
        typeof(AnalysisResult).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    public static string FormatMetricsVersion(int metricsVersion) =>
        metricsVersion == 0 ? "unknown" : metricsVersion.ToString();
}
