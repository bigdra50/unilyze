using Unilyze.Metrics;
using System.Text.Json.Serialization;

namespace Unilyze.Config;

internal sealed record CalibrateResult(
    string Methodology,
    int MetricsVersion,
    string ToolVersion,
    IReadOnlyList<CalibrateSourceInfo> Sources,
    CalibrateRiskCategories RiskCategories,
    CalibrateMetricsBlock Metrics,
    CalibrateUnilyzeConfigFragment UnilyzeConfigFragment);

internal sealed record CalibrateSourceInfo(
    string FileName,
    string ProjectPath,
    int MethodCount,
    int TypeCount,
    int TotalMethodLoc);

internal sealed record CalibrateRiskCategories(
    string Low,
    string Moderate,
    string High,
    string VeryHigh);

internal sealed record CalibrateMetricsBlock(
    CalibrateMetricThresholds MethodLines,
    CalibrateMetricThresholds CyclomaticComplexity,
    CalibrateMetricThresholds CognitiveComplexity,
    CalibrateMetricThresholds MaxNestingDepth,
    CalibrateParameterThresholds ParameterCount,
    CalibrateMetricThresholds MethodsPerType,
    CalibrateMetricThresholds TypeLines);

internal sealed record CalibrateMetricThresholds(
    string Unit,
    IReadOnlyList<double> Percentiles,
    IReadOnlyList<int> PercentileLevels,
    CalibrateRiskBands RiskBands);

internal sealed record CalibrateParameterThresholds(
    string Unit,
    IReadOnlyList<double> Percentiles,
    IReadOnlyList<int> PercentileLevels,
    CalibrateRiskBands RiskBands);

internal sealed record CalibrateRiskBands(
    double LowUpper,
    double ModerateUpper,
    double HighUpper);

internal sealed record CalibrateUnilyzeConfigFragment(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Smells);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CalibrateResult))]
internal partial class CalibrateJsonContext : JsonSerializerContext;
