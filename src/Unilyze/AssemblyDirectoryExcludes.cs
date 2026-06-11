namespace Unilyze;

internal static class AssemblyDirectoryExcludes
{
    public static List<AsmdefInfo> ApplyNested(IReadOnlyList<AsmdefInfo> assemblies)
    {
        var fullDirs = assemblies
            .Select(asm => (Asm: asm, FullDir: Path.GetFullPath(asm.Directory)))
            .ToList();

        var updated = new List<AsmdefInfo>(assemblies.Count);
        foreach (var (asm, fullDir) in fullDirs)
        {
            var nestedDirs = fullDirs
                .Where(other => !other.FullDir.Equals(fullDir, StringComparison.OrdinalIgnoreCase)
                    && DefaultExcludes.IsStrictSubdirectory(other.FullDir, fullDir))
                .Select(other => other.FullDir)
                .ToList();

            if (nestedDirs.Count == 0 && asm.ExcludeDirectories is not { Count: > 0 })
            {
                updated.Add(asm);
                continue;
            }

            var merged = asm.ExcludeDirectories?.ToList() ?? [];
            foreach (var nestedDir in nestedDirs)
            {
                if (!merged.Any(existing => existing.Equals(nestedDir, StringComparison.OrdinalIgnoreCase)))
                    merged.Add(nestedDir);
            }

            updated.Add(asm with { ExcludeDirectories = merged });
        }

        return updated;
    }
}
