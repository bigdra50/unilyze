namespace Unilyze.Discovery;

internal static class TargetFrameworkSelector
{
    public static string? Select(
        IReadOnlyList<string> availableTfms,
        IReadOnlyList<string>? csprojTfms,
        string? explicitTfm,
        string? runtimeTfm)
    {
        if (availableTfms.Count == 0)
            return null;

        if (explicitTfm is { Length: > 0 })
            return TryMatch(availableTfms, explicitTfm);

        if (csprojTfms is { Count: > 0 })
        {
            foreach (var tfm in csprojTfms)
            {
                var matched = TryMatch(availableTfms, tfm);
                if (matched is not null)
                    return matched;
            }
        }

        if (runtimeTfm is not null)
        {
            var matched = TryMatch(availableTfms, runtimeTfm);
            if (matched is not null)
                return matched;
        }

        return availableTfms
            .OrderByDescending(ParseFrameworkVersion)
            .ThenBy(t => t, StringComparer.Ordinal)
            .First();
    }

    static string? TryMatch(IReadOnlyList<string> availableTfms, string candidate)
    {
        foreach (var tfm in availableTfms)
        {
            if (tfm.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return tfm;
        }

        return null;
    }

    static Version ParseFrameworkVersion(string tfm)
    {
        if (!tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            return new Version(0, 0);

        var body = tfm[3..];
        var dash = body.IndexOf('-');
        if (dash >= 0)
            body = body[..dash];

        var parts = body.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        return new Version(major, minor);
    }

    internal static string? GetRunningTargetFramework()
    {
        var framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        if (framework.Contains("10.", StringComparison.Ordinal))
            return "net10.0";
        if (framework.Contains("9.", StringComparison.Ordinal))
            return "net9.0";
        if (framework.Contains("8.", StringComparison.Ordinal))
            return "net8.0";
        return null;
    }

    public static string? ResolveForProject(
        IReadOnlyList<string> csprojFiles,
        IReadOnlyList<string>? csprojTargetFrameworks,
        string? explicitTfm)
    {
        var availableTfms = CollectAvailableTargetFrameworks(csprojFiles);
        if (availableTfms.Count == 0)
            return null;

        return Select(
            availableTfms,
            csprojTargetFrameworks,
            explicitTfm,
            GetRunningTargetFramework());
    }

    static List<string> CollectAvailableTargetFrameworks(IReadOnlyList<string> csprojFiles)
    {
        var availableTfms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var csproj in csprojFiles)
            AddTfmsFromCsproj(csproj, availableTfms);

        return availableTfms.OrderBy(t => t, StringComparer.Ordinal).ToList();
    }

    static void AddTfmsFromCsproj(string csproj, HashSet<string> availableTfms)
    {
        var csprojDir = Path.GetDirectoryName(Path.GetFullPath(csproj)) ?? ".";
        var assetsPath = Path.Combine(csprojDir, "obj", "project.assets.json");
        if (ProjectAssetsJsonReader.TryRead(assetsPath, out var document) && document is not null)
        {
            foreach (var tfm in document.AvailableTargetFrameworks)
                availableTfms.Add(tfm);
        }

        AddGeneratedTfms(csprojDir, availableTfms);
    }

    static void AddGeneratedTfms(string csprojDir, HashSet<string> availableTfms)
    {
        var objDir = Path.Combine(csprojDir, "obj");
        if (!Directory.Exists(objDir))
            return;

        foreach (var configDir in Directory.EnumerateDirectories(objDir))
            AddGeneratedTfmsFromConfig(configDir, availableTfms);
    }

    static void AddGeneratedTfmsFromConfig(string configDir, HashSet<string> availableTfms)
    {
        foreach (var tfmDir in Directory.EnumerateDirectories(configDir))
        {
            if (Directory.Exists(Path.Combine(tfmDir, "generated")))
                availableTfms.Add(Path.GetFileName(tfmDir));
        }
    }
}
