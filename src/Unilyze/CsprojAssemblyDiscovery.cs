namespace Unilyze;

internal static class CsprojAssemblyDiscovery
{
    public static IReadOnlyList<AsmdefInfo> Discover(
        string projectRoot,
        IReadOnlyList<string>? excludeDirectories = null)
    {
        var csprojFiles = CsprojParser.DiscoverCsprojFiles(projectRoot, excludeDirectories);
        if (csprojFiles.Count == 0)
            return [];

        var nameByPath = csprojFiles.ToDictionary(
            path => Path.GetFullPath(path),
            path => Path.GetFileNameWithoutExtension(path),
            StringComparer.OrdinalIgnoreCase);

        var results = new List<AsmdefInfo>(csprojFiles.Count);
        foreach (var csprojPath in csprojFiles)
        {
            var info = CsprojParser.TryParse(csprojPath);
            if (info is null)
                continue;

            var directory = Path.GetDirectoryName(Path.GetFullPath(csprojPath)) ?? projectRoot;
            var name = Path.GetFileNameWithoutExtension(csprojPath);
            var (references, unresolved) = MapProjectReferences(directory, info.ProjectReferences, nameByPath);
            results.Add(new AsmdefInfo(
                name,
                directory,
                references,
                unresolved.Count > 0 ? unresolved : null));
        }

        if (results.Count == 0)
            return [];

        results = AssemblyDirectoryExcludes.ApplyNested(results);

        if (HasLooseFiles(projectRoot, results, excludeDirectories))
        {
            var csprojDirs = results.Select(a => Path.GetFullPath(a.Directory)).ToList();
            var refs = results.Select(a => a.Name).ToList();
            results.Add(new AsmdefInfo("Assembly-CSharp", projectRoot, refs, ExcludeDirectories: csprojDirs));
        }

        return results;
    }

    static (List<string> References, List<string> UnresolvedReferences) MapProjectReferences(
        string csprojDirectory,
        IReadOnlyList<string> projectReferences,
        IReadOnlyDictionary<string, string> nameByPath)
    {
        var references = new List<string>();
        var unresolved = new List<string>();

        foreach (var projectRef in projectReferences)
        {
            var refPath = Path.GetFullPath(Path.Combine(
                csprojDirectory,
                projectRef.Replace('\\', Path.DirectorySeparatorChar)));

            if (nameByPath.TryGetValue(refPath, out var refName))
                references.Add(refName);
            else
                unresolved.Add(projectRef);
        }

        return (references, unresolved);
    }

    static bool HasLooseFiles(
        string projectRoot,
        IReadOnlyList<AsmdefInfo> assemblies,
        IReadOnlyList<string>? excludeDirectories)
    {
        var csprojDirs = assemblies
            .Select(a => Path.GetFullPath(a.Directory) + Path.DirectorySeparatorChar)
            .ToList();

        try
        {
            foreach (var csFile in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (DefaultExcludes.ShouldExcludeSourceFile(
                        csFile, excludeDirectories, excludeGeneratedCode: true, projectRoot, applyAnyDepthExcludes: true))
                    continue;

                var fullPath = Path.GetFullPath(csFile);
                if (!csprojDirs.Any(dir => fullPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }
}
