using System.Text.Json;

namespace Unilyze;

internal static class ProjectAssetsJsonReader
{
    internal sealed record AssetsDocument(
        IReadOnlyList<string> PackageFolders,
        IReadOnlyList<string> AvailableTargetFrameworks,
        IReadOnlyDictionary<string, PackageLibrary> Libraries,
        IReadOnlyDictionary<string, JsonElement> TargetsByFramework);

    internal sealed record PackageLibrary(string Path);

    internal sealed record CompileAsset(string PackageId, string RelativePath);

    public static bool TryRead(string assetsPath, out AssetsDocument? document)
    {
        document = null;
        if (!File.Exists(assetsPath))
            return false;

        try
        {
            using var stream = File.OpenRead(assetsPath);
            using var json = JsonDocument.Parse(stream);
            document = Parse(json.RootElement);
            return document is not null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static IReadOnlyList<CompileAsset> CollectCompileAssets(
        AssetsDocument document,
        string targetFramework)
    {
        if (!document.TargetsByFramework.TryGetValue(targetFramework, out var targetElement))
            return [];

        var assets = new List<CompileAsset>();
        foreach (var packageProperty in targetElement.EnumerateObject())
        {
            if (!packageProperty.Value.TryGetProperty("type", out var typeProp)
                || typeProp.GetString() != "package")
                continue;

            if (!packageProperty.Value.TryGetProperty("compile", out var compileProp)
                || compileProp.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var compileEntry in compileProp.EnumerateObject())
            {
                if (compileEntry.NameEquals("_._"))
                    continue;
                assets.Add(new CompileAsset(packageProperty.Name, compileEntry.Name));
            }
        }

        return assets;
    }

    public static IReadOnlyList<string> ResolveDllPaths(
        AssetsDocument document,
        IReadOnlyList<CompileAsset> compileAssets)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in compileAssets)
        {
            if (!document.Libraries.TryGetValue(asset.PackageId, out var library))
                continue;

            foreach (var folder in document.PackageFolders)
            {
                var fullPath = Path.GetFullPath(Path.Combine(folder, library.Path, asset.RelativePath));
                if (!fullPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (File.Exists(fullPath))
                    paths.Add(fullPath);
            }
        }

        return paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static AssetsDocument? Parse(JsonElement root)
    {
        var packageFolders = ReadPackageFolders(root);
        if (packageFolders.Count == 0)
            return null;

        var libraries = ReadLibraries(root);
        var targetsByFramework = ReadTargets(root, out var availableTfms);
        if (availableTfms.Count == 0)
            return null;

        return new AssetsDocument(packageFolders, availableTfms, libraries, targetsByFramework);
    }

    static List<string> ReadPackageFolders(JsonElement root)
    {
        var folders = new List<string>();
        if (!root.TryGetProperty("packageFolders", out var foldersElement)
            || foldersElement.ValueKind != JsonValueKind.Object)
            return folders;

        foreach (var folder in foldersElement.EnumerateObject())
            folders.Add(folder.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (folders.Count == 0
            && root.TryGetProperty("project", out var project)
            && project.TryGetProperty("restore", out var restore)
            && restore.TryGetProperty("packagesPath", out var packagesPath))
        {
            var path = packagesPath.GetString();
            if (!string.IsNullOrWhiteSpace(path))
                folders.Add(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return folders;
    }

    static Dictionary<string, PackageLibrary> ReadLibraries(JsonElement root)
    {
        var libraries = new Dictionary<string, PackageLibrary>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("libraries", out var librariesElement)
            || librariesElement.ValueKind != JsonValueKind.Object)
            return libraries;

        foreach (var library in librariesElement.EnumerateObject())
        {
            if (!library.Value.TryGetProperty("path", out var pathProp))
                continue;
            var path = pathProp.GetString();
            if (string.IsNullOrWhiteSpace(path))
                continue;
            libraries[library.Name] = new PackageLibrary(path);
        }

        return libraries;
    }

    static Dictionary<string, JsonElement> ReadTargets(JsonElement root, out List<string> availableTfms)
    {
        availableTfms = [];
        var targetsByFramework = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("targets", out var targetsElement)
            || targetsElement.ValueKind != JsonValueKind.Object)
            return targetsByFramework;

        foreach (var tfmEntry in targetsElement.EnumerateObject())
        {
            if (!IsTargetFrameworkKey(tfmEntry.Name))
                continue;
            availableTfms.Add(tfmEntry.Name);
            targetsByFramework[tfmEntry.Name] = tfmEntry.Value.Clone();
        }

        return targetsByFramework;
    }

    static bool IsTargetFrameworkKey(string key) =>
        key.StartsWith("net", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith(".NET", StringComparison.OrdinalIgnoreCase);
}
