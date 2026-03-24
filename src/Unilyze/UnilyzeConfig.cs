using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unilyze;

internal sealed record UnilyzeConfig(
    [property: JsonPropertyName("excludeDirs")]
    IReadOnlyList<string>? ExcludeDirs = null)
{
    public static UnilyzeConfig Empty { get; } = new();

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    public static UnilyzeConfig LoadMerged(
        string projectRoot,
        IReadOnlyList<string>? cliExcludeDirs = null)
    {
        var global = LoadFile(GetGlobalConfigPath());
        var project = LoadFile(GetProjectConfigPath(projectRoot));
        var merged = Merge(global, project);

        if (cliExcludeDirs is { Count: > 0 })
            merged = Merge(merged, new UnilyzeConfig(cliExcludeDirs));

        var resolved = merged.ExcludeDirs is { Count: > 0 }
            ? ResolveExcludePaths(merged.ExcludeDirs, projectRoot)
            : null;

        return merged with { ExcludeDirs = resolved };
    }

    internal static UnilyzeConfig LoadFile(string path)
    {
        if (!File.Exists(path))
            return Empty;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UnilyzeConfig>(json, JsonOptions) ?? Empty;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: Failed to load config {path}: {ex.Message}");
            return Empty;
        }
    }

    internal static UnilyzeConfig Merge(UnilyzeConfig lower, UnilyzeConfig higher)
    {
        if (lower.ExcludeDirs is not { Count: > 0 })
            return higher;
        if (higher.ExcludeDirs is not { Count: > 0 })
            return lower;

        var merged = new HashSet<string>(lower.ExcludeDirs, StringComparer.OrdinalIgnoreCase);
        foreach (var dir in higher.ExcludeDirs)
            merged.Add(dir);

        return new UnilyzeConfig(merged.ToList());
    }

    internal static IReadOnlyList<string> ResolveExcludePaths(
        IReadOnlyList<string> excludeDirs, string projectRoot)
    {
        var resolved = new List<string>(excludeDirs.Count);
        foreach (var dir in excludeDirs)
        {
            var full = Path.IsPathRooted(dir)
                ? Path.GetFullPath(dir)
                : Path.GetFullPath(Path.Combine(projectRoot, dir));
            resolved.Add(full);
        }
        return resolved;
    }

    internal static string GetGlobalConfigPath()
    {
        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(xdgConfig))
            xdgConfig = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        return Path.Combine(xdgConfig, "unilyze", "config.json");
    }

    internal static string GetProjectConfigPath(string projectRoot)
        => Path.Combine(projectRoot, ".unilyze.json");
}
