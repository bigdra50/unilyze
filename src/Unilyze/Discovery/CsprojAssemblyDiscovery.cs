using Unilyze.Pipeline;

namespace Unilyze.Discovery;

internal static class CsprojAssemblyDiscovery
{
    public static IReadOnlyList<AsmdefInfo> Discover(
        string projectRoot,
        IReadOnlyList<string>? excludeDirectories = null,
        bool applyAnyDepthExcludes = true,
        IAnalysisLogSink? log = null)
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

            List<string>? sourceFiles = null;
            if (info.CompileItems is { } compileItems)
            {
                sourceFiles = SelectListedSources(compileItems, directory, excludeDirectories, applyAnyDepthExcludes);
                if (sourceFiles.Count == 0)
                {
                    // Unity's package projects list only Library/PackageCache sources; with
                    // those excluded there is nothing to analyze, as in asmdef discovery,
                    // which never sees package assemblies either.
                    log?.Info($"Skipping {name}: every source it lists is missing or excluded");
                    continue;
                }
            }

            results.Add(new AsmdefInfo(
                name,
                directory,
                references,
                unresolved.Count > 0 ? unresolved : null,
                SourceFiles: sourceFiles));
        }

        if (results.Count == 0)
            return [];

        results = DropDirectorySharers(results, log);
        results = AssemblyDirectoryExcludes.ApplyNested(results);

        if (HasLooseFiles(projectRoot, results, excludeDirectories, applyAnyDepthExcludes))
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

    static List<string> SelectListedSources(
        IReadOnlyList<string> compileItems,
        string csprojDirectory,
        IReadOnlyList<string>? excludeDirectories,
        bool applyAnyDepthExcludes)
    {
        // Generated-code detection reads file heads; it runs once, when the files are parsed.
        return compileItems
            .Where(File.Exists)
            .Where(file => !DefaultExcludes.ShouldExcludeSourceFile(
                file, excludeDirectories, excludeGeneratedCode: false, csprojDirectory, applyAnyDepthExcludes))
            .ToList();
    }

    // Several directory-globbing projects in one directory each compile the whole directory.
    // MSBuild tolerates that, but analyzing the same files once per project multiplies the
    // work and reports every type once per project. The first project by name keeps the
    // directory; projects that list their own files are unaffected.
    static List<AsmdefInfo> DropDirectorySharers(List<AsmdefInfo> assemblies, IAnalysisLogSink? log)
    {
        var ownerByDirectory = assemblies
            .Where(asm => asm.SourceFiles is null)
            .GroupBy(asm => Path.GetFullPath(asm.Directory), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(asm => asm.Name, StringComparer.Ordinal).First(),
                StringComparer.OrdinalIgnoreCase);

        var kept = new List<AsmdefInfo>(assemblies.Count);
        foreach (var asm in assemblies)
        {
            if (asm.SourceFiles is null)
            {
                var owner = ownerByDirectory[Path.GetFullPath(asm.Directory)];
                if (!ReferenceEquals(owner, asm))
                {
                    log?.Warning($"Warning: {asm.Name} shares its directory with {owner.Name}; the directory is analyzed once, as {owner.Name}");
                    continue;
                }
            }

            kept.Add(asm);
        }

        return kept;
    }

    static bool HasLooseFiles(
        string projectRoot,
        IReadOnlyList<AsmdefInfo> assemblies,
        IReadOnlyList<string>? excludeDirectories,
        bool applyAnyDepthExcludes)
    {
        var csprojDirs = assemblies
            .Select(a => Path.GetFullPath(a.Directory) + Path.DirectorySeparatorChar)
            .ToList();

        try
        {
            foreach (var csFile in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (DefaultExcludes.ShouldExcludeSourceFile(
                        csFile, excludeDirectories, excludeGeneratedCode: true, projectRoot, applyAnyDepthExcludes))
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
