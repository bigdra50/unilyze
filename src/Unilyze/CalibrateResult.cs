using System.Text.Json.Serialization;

namespace Unilyze;

public sealed record CalibrateResult(
    string Methodology,
    int MetricsVersion,
    string ToolVersion,
    IReadOnlyList<CalibrateSourceInfo> Sources,
    CalibrateRiskCategories RiskCategories,
    CalibrateMetricsBlock Metrics,
    CalibrateUnilyzeConfigFragment UnilyzeConfigFragment);

public sealed record CalibrateSourceInfo(
    string FileName,
    string ProjectPath,
    int MethodCount,
    int TypeCount,
    int TotalMethodLoc);

public sealed record CalibrateRiskCategories(
    string Low,
    string Moderate,
    string High,
    string VeryHigh);

public sealed record CalibrateMetricsBlock(
    CalibrateMetricThresholds MethodLines,
    CalibrateMetricThresholds CyclomaticComplexity,
    CalibrateMetricThresholds CognitiveComplexity,
    CalibrateMetricThresholds MaxNestingDepth,
    CalibrateParameterThresholds ParameterCount,
    CalibrateMetricThresholds MethodsPerType,
    CalibrateMetricThresholds TypeLines);

public sealed record CalibrateMetricThresholds(
    string Unit,
    IReadOnlyList<double> Percentiles,
    IReadOnlyList<int> PercentileLevels,
    CalibrateRiskBands RiskBands);

public sealed record CalibrateParameterThresholds(
    string Unit,
    IReadOnlyList<double> Percentiles,
    IReadOnlyList<int> PercentileLevels,
    CalibrateRiskBands RiskBands);

public sealed record CalibrateRiskBands(
    double LowUpper,
    double ModerateUpper,
    double HighUpper);

public sealed record CalibrateUnilyzeConfigFragment(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Smells);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CalibrateResult))]
internal partial class CalibrateJsonContext : JsonSerializerContext;
