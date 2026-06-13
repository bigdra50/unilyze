using Unilyze.Pipeline;
namespace Unilyze.Discovery;

internal static class NuGetAssetsReferenceResolver
{
    public static IReadOnlyList<string> Resolve(
        IReadOnlyList<string> csprojFiles,
        IReadOnlyList<string>? csprojTargetFrameworks,
        string? explicitTfm,
        string? selectedTfm,
        IAnalysisLogSink log)
    {
        var tfm = selectedTfm ?? TargetFrameworkSelector.ResolveForProject(csprojFiles, csprojTargetFrameworks, explicitTfm);
        if (tfm is null)
            return [];

        var dllPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var csproj in csprojFiles)
        {
            var csprojDir = Path.GetDirectoryName(Path.GetFullPath(csproj)) ?? ".";
            var assetsPath = Path.Combine(csprojDir, "obj", "project.assets.json");
            if (!ProjectAssetsJsonReader.TryRead(assetsPath, out var document) || document is null)
                continue;

            var compileAssets = ProjectAssetsJsonReader.CollectCompileAssets(document, tfm);
            foreach (var path in ProjectAssetsJsonReader.ResolveDllPaths(document, compileAssets))
                dllPaths.Add(path);
        }

        var resolved = dllPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        if (resolved.Count > 0)
            log.Info($"NuGet references: {resolved.Count} package DLL(s), TFM {tfm}");

        return resolved;
    }
}
