namespace Unilyze;

public static class DotnetRuntimeReferenceResolver
{
    // maxLevel caps reference collection so the resolved level is pinned deterministically.
    public static ResolvedDlls Resolve(AnalysisLevel maxLevel = AnalysisLevel.Complete)
    {
        if (maxLevel == AnalysisLevel.Syntax)
            return new ResolvedDlls(AnalysisLevel.Syntax, []);

        var paths = CollectFrameworkAssemblyPaths();
        if (paths.Count == 0)
            return new ResolvedDlls(AnalysisLevel.Syntax, []);

        // BCL injection is the maximum semantic depth available for non-Unity projects.
        var level = AnalysisLevel.Complete;
        if (maxLevel < level)
            level = maxLevel;

        return new ResolvedDlls(level, paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    internal static bool IsFrameworkAssembly(string assemblyPath)
    {
        var name = Path.GetFileNameWithoutExtension(assemblyPath);
        return name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
            || name.Equals("netstandard", StringComparison.OrdinalIgnoreCase)
            || name.Equals("System.Private.CoreLib", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("System.", StringComparison.Ordinal);
    }

    static List<string> CollectFrameworkAssemblyPaths()
    {
        var paths = new List<string>();

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (IsFrameworkAssembly(path) && File.Exists(path))
                    paths.Add(path);
            }
        }

        if (paths.Count > 0)
            return paths;

        var coreLib = typeof(object).Assembly.Location;
        if (string.IsNullOrEmpty(coreLib))
            return paths;

        var runtimeDir = Path.GetDirectoryName(coreLib);
        if (runtimeDir is null || !Directory.Exists(runtimeDir))
            return paths;

        foreach (var dll in Directory.EnumerateFiles(runtimeDir, "*.dll"))
        {
            if (IsFrameworkAssembly(dll))
                paths.Add(dll);
        }

        return paths;
    }
}
