using System.Text.Json;

namespace Unilyze;

public enum AnalysisLevel
{
    SyntaxOnly,
    CoreEngine,
    FullEngine,
    Complete
}

public sealed record ResolvedDlls(
    AnalysisLevel Level,
    IReadOnlyList<string> Paths);

public static class UnityDllResolver
{
    public static ResolvedDlls Resolve(string projectRoot)
    {
        var version = DetectUnityVersion(projectRoot);
        if (version is null)
            return new ResolvedDlls(AnalysisLevel.SyntaxOnly, []);

        var editorPath = FindEditorInstallPath(version);
        if (editorPath is null)
            return new ResolvedDlls(AnalysisLevel.SyntaxOnly, []);

        var contentsRoot = ResolveContentsRoot(editorPath);
        if (contentsRoot is null)
            return new ResolvedDlls(AnalysisLevel.SyntaxOnly, []);

        var managedRoot = ResolveManagedRoot(contentsRoot);
        if (managedRoot is null)
            return new ResolvedDlls(AnalysisLevel.SyntaxOnly, []);

        var paths = new List<string>();

        var coreDlls = CollectCoreDlls(managedRoot, contentsRoot);
        paths.AddRange(coreDlls);
        if (paths.Count == 0)
            return new ResolvedDlls(AnalysisLevel.SyntaxOnly, []);

        var moduleDlls = CollectModuleDlls(managedRoot);
        paths.AddRange(moduleDlls);

        var scriptAssembliesDir = Path.Combine(projectRoot, "Library", "ScriptAssemblies");
        var packageDlls = CollectPackageDlls(scriptAssembliesDir);

        var level = packageDlls.Count > 0 ? AnalysisLevel.Complete :
                    moduleDlls.Count > 0 ? AnalysisLevel.FullEngine :
                    AnalysisLevel.CoreEngine;

        paths.AddRange(packageDlls);

        return new ResolvedDlls(level, paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static IReadOnlyList<string> GetPreprocessorDefines(string projectRoot)
    {
        var version = DetectUnityVersion(projectRoot);
        if (version is null) return [];

        var defines = new List<string> { "UNITY_EDITOR" };

        // Parse major version from "2022.3.45f1" or "6000.3.0f1"
        var dotIndex = version.IndexOf('.');
        if (dotIndex > 0 && int.TryParse(version[..dotIndex], out _))
            defines.Add($"UNITY_{version[..dotIndex]}");

        return defines;
    }

    static string? DetectUnityVersion(string projectRoot)
    {
        var versionFile = Path.Combine(projectRoot, "ProjectSettings", "ProjectVersion.txt");
        if (!File.Exists(versionFile)) return null;

        foreach (var line in File.ReadLines(versionFile))
        {
            if (!line.StartsWith("m_EditorVersion:")) continue;
            var version = line["m_EditorVersion:".Length..].Trim();
            return string.IsNullOrEmpty(version) ? null : version;
        }

        return null;
    }

    // --- Editor install path resolution ---

    static string? FindEditorInstallPath(string version)
    {
        return GetHubEditorRoots()
            .Select(root => Path.Combine(root, version))
            .FirstOrDefault(Directory.Exists);
    }

    static IEnumerable<string> GetHubEditorRoots()
    {
        // 1. Environment variable (highest priority)
        // Points to the parent directory containing version folders (e.g. /Applications/Unity/Hub/Editor)
        var envPath = Environment.GetEnvironmentVariable("UNILYZE_EDITORS_ROOT")
                   ?? Environment.GetEnvironmentVariable("UNITY_EDITOR_PATH");
        if (!string.IsNullOrEmpty(envPath))
            yield return envPath;

        // 2. Unity Hub config: custom install path
        var hubConfigPath = GetHubConfigPath();
        if (hubConfigPath is not null)
        {
            var secondaryPath = ReadHubSecondaryInstallPath(hubConfigPath);
            if (secondaryPath is not null)
                yield return secondaryPath;
        }

        // 3. Default Hub install locations (OS-specific)
        foreach (var root in GetDefaultEditorRoots())
            yield return root;
    }

    static IEnumerable<string> GetDefaultEditorRoots()
    {
        var roots = OperatingSystem.IsMacOS() ? GetMacEditorRoots()
                  : OperatingSystem.IsLinux() ? GetLinuxEditorRoots()
                  : OperatingSystem.IsWindows() ? GetWindowsDriveEditorRoots()
                  : Enumerable.Empty<string>();

        foreach (var root in roots)
            yield return root;
    }

    static IEnumerable<string> GetMacEditorRoots()
    {
        yield return "/Applications/Unity/Hub/Editor";
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Unity", "Hub", "Editor");
    }

    static IEnumerable<string> GetLinuxEditorRoots()
    {
        yield return "/opt/Unity/Hub/Editor";
        yield return "/usr/local/Unity/Hub/Editor";
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Unity", "Hub", "Editor");
    }

    static IEnumerable<string> GetWindowsDriveEditorRoots()
    {
        // C: first for consistency with typical install location
        yield return @"C:\Program Files\Unity\Hub\Editor";

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed) continue;
            yield return Path.Combine(drive.Name, "Program Files", "Unity", "Hub", "Editor");
        }
    }

    static string? GetHubConfigPath()
    {
        var configDir = GetHubConfigDir();
        return Directory.Exists(configDir) ? configDir : null;
    }

    static string GetHubConfigDir()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "UnityHub");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Application Support", "UnityHub")
            : Path.Combine(home, ".config", "UnityHub");
    }

    static string? ReadHubSecondaryInstallPath(string hubConfigDir)
    {
        var file = Path.Combine(hubConfigDir, "secondaryInstallPath.json");
        if (!File.Exists(file)) return null;

        try
        {
            var content = File.ReadAllText(file).Trim();
            // JSON deserialization handles escape sequences (e.g. "D:\\Unity")
            var path = JsonSerializer.Deserialize<string>(content) ?? content.Trim('"');
            return !string.IsNullOrEmpty(path) && Directory.Exists(path) ? path : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // --- Contents root resolution (OS abstraction) ---

    static string? ResolveContentsRoot(string editorPath)
    {
        // macOS: Editor/<ver>/Unity.app/Contents/
        // Windows/Linux: Editor/<ver>/Editor/Data/
        var candidates = new[]
        {
            Path.Combine(editorPath, "Unity.app", "Contents"),
            Path.Combine(editorPath, "Editor", "Data"),
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    // --- Managed root resolution ---

    static string? ResolveManagedRoot(string contentsRoot)
    {
        // Unity 6000.3+ moved to Resources/Scripting/Managed/
        // Older versions use Managed/ directly
        var candidates = new[]
        {
            Path.Combine(contentsRoot, "Resources", "Scripting", "Managed"),
            Path.Combine(contentsRoot, "Managed"),
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    // --- DLL collection ---

    static List<string> CollectCoreDlls(string managedRoot, string contentsRoot)
    {
        var paths = new List<string>();

        AddIfExists(paths, Path.Combine(managedRoot, "UnityEngine.dll"));
        var engineModulesDir = Path.Combine(managedRoot, "UnityEngine");
        AddIfExists(paths, Path.Combine(engineModulesDir, "UnityEngine.CoreModule.dll"));
        AddIfExists(paths, Path.Combine(engineModulesDir, "UnityEngine.SharedInternalsModule.dll"));

        // netstandard.dll / mscorlib.dll: try all known relative paths under contentsRoot
        var frameworkDlls = new[]
        {
            Path.Combine("Resources", "Scripting", "NetStandard", "ref", "2.1.0", "netstandard.dll"),
            Path.Combine("NetStandard", "ref", "2.1.0", "netstandard.dll"),
            Path.Combine("MonoBleedingEdge", "lib", "mono", "4.7.1-api", "Facades", "netstandard.dll"),
            Path.Combine("Resources", "Scripting", "NetStandard", "compat", "2.1.0", "shims", "netfx", "mscorlib.dll"),
            Path.Combine("MonoBleedingEdge", "lib", "mono", "4.7.1-api", "mscorlib.dll"),
        };

        foreach (var relative in frameworkDlls)
            AddIfExists(paths, Path.Combine(contentsRoot, relative));

        return paths;
    }

    static List<string> CollectModuleDlls(string managedRoot)
    {
        var paths = new List<string>();

        CollectDllsInDir(paths, Path.Combine(managedRoot, "UnityEngine"),
            exclude: ["UnityEngine.CoreModule.dll", "UnityEngine.SharedInternalsModule.dll"]);

        AddIfExists(paths, Path.Combine(managedRoot, "UnityEditor.dll"));
        CollectDllsInDir(paths, Path.Combine(managedRoot, "UnityEditor"));

        return paths;
    }

    static List<string> CollectPackageDlls(string scriptAssembliesDir)
    {
        if (!Directory.Exists(scriptAssembliesDir))
            return [];

        return Directory.EnumerateFiles(scriptAssembliesDir, "*.dll").ToList();
    }

    // --- Helpers ---

    static void CollectDllsInDir(List<string> paths, string dir, string[]? exclude = null)
    {
        if (!Directory.Exists(dir)) return;

        var excludeSet = exclude is not null
            ? new HashSet<string>(exclude, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
        {
            if (excludeSet?.Contains(Path.GetFileName(dll)) == true) continue;
            paths.Add(dll);
        }
    }

    static void AddIfExists(List<string> paths, string path)
    {
        if (File.Exists(path)) paths.Add(path);
    }
}
