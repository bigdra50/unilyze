namespace Unilyze;

internal static class ProgramHelpers
{
    public static Dictionary<string, string> ParseOptions(string[] args)
    {
        var opts = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith('-'))
            {
                if (args[i] is "-h" or "--help" or "-v" or "--version" or "--no-open")
                    opts[args[i]] = "true";
                else if (i + 1 < args.Length)
                {
                    opts[args[i]] = args[i + 1];
                    i++;
                }
            }
        }
        return opts;
    }

    public static OutputFormat ResolveFormat(string? formatStr, string? output)
    {
        if (formatStr != null)
        {
            return formatStr.ToLowerInvariant() switch
            {
                "json" => OutputFormat.Json,
                "html" => OutputFormat.Html,
                "sarif" => OutputFormat.Sarif,
                _ => throw new ArgumentException($"Unknown format: '{formatStr}'. Valid formats: json, html, sarif")
            };
        }

        if (output != null)
        {
            return Path.GetExtension(output).ToLowerInvariant() switch
            {
                ".html" or ".htm" => OutputFormat.Html,
                ".json" => OutputFormat.Json,
                ".sarif" => OutputFormat.Sarif,
                _ => OutputFormat.Json
            };
        }

        return OutputFormat.Html;
    }

    public static IReadOnlyList<AsmdefInfo> FilterAssemblies(
        IReadOnlyList<AsmdefInfo> asmdefs, string? prefix, string? assemblyFilter)
    {
        if (assemblyFilter != null)
        {
            var filtered = asmdefs.Where(a =>
                a.Name.Equals(assemblyFilter, StringComparison.OrdinalIgnoreCase)
                || a.Name.EndsWith("." + assemblyFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (filtered.Count == 0)
                throw new InvalidOperationException($"Assembly '{assemblyFilter}' not found.");
            return filtered;
        }

        if (prefix != null)
            return asmdefs.Where(a => a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

        return asmdefs.ToList();
    }

    public static string? DetectCommonPrefix(IReadOnlyList<AsmdefInfo> asmdefs)
    {
        var names = asmdefs.Select(a => a.Name).ToList();
        if (names.Count < 2) return null;
        var parts = names[0].Split('.');
        for (var len = parts.Length; len > 0; len--)
        {
            var candidate = string.Join(".", parts.Take(len)) + ".";
            if (names.All(n => n.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
        return null;
    }

    public static string ResolveAssetsDir(string path)
    {
        if (Directory.Exists(Path.Combine(path, "Assets")))
            return Path.Combine(path, "Assets");
        if (Path.GetFileName(path) == "Assets")
            return path;
        return path;
    }

    public static string ResolveProjectRoot(string path)
    {
        var dir = Path.GetFullPath(path);
        for (var i = 0; i < 5; i++)
        {
            if (File.Exists(Path.Combine(dir, "ProjectSettings", "ProjectVersion.txt")))
                return dir;
            if (Directory.EnumerateFiles(dir, "*.sln", SearchOption.TopDirectoryOnly).Any())
                return dir;
            if (Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).Any())
                return dir;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent is null || parent == dir) break;
            dir = parent;
        }

        return Path.GetFullPath(path);
    }
}
