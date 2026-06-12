using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unilyze;

internal readonly record struct ReferenceAnalysisSettings(
    [property: JsonPropertyName("resolveNuget")] bool ResolveNuget = false,
    [property: JsonPropertyName("includeGenerated")] bool IncludeGenerated = false,
    [property: JsonPropertyName("targetFramework")] string? TargetFramework = null)
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    internal static ReferenceAnalysisSettings LoadMerged(
        string projectRoot,
        bool cliResolveNuget = false,
        bool cliIncludeGenerated = false,
        string? cliTargetFramework = null)
    {
        var global = LoadFile(UnilyzeConfig.GetGlobalConfigPath());
        var project = LoadFile(UnilyzeConfig.GetProjectConfigPath(projectRoot));
        var merged = Merge(global, project);
        return new ReferenceAnalysisSettings(
            merged.ResolveNuget || cliResolveNuget,
            merged.IncludeGenerated || cliIncludeGenerated,
            cliTargetFramework ?? merged.TargetFramework);
    }

    static ReferenceAnalysisSettings LoadFile(string path)
    {
        if (!File.Exists(path))
            return default;

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<ReferenceAnalysisSettings>(json, JsonOptions);
            return parsed;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: Failed to load reference settings from {path}: {ex.Message}");
            return default;
        }
    }

    static ReferenceAnalysisSettings Merge(ReferenceAnalysisSettings lower, ReferenceAnalysisSettings higher) =>
        new(
            lower.ResolveNuget || higher.ResolveNuget,
            lower.IncludeGenerated || higher.IncludeGenerated,
            higher.TargetFramework ?? lower.TargetFramework);
}
