using System.Xml.Linq;

namespace Unilyze.Discovery;

/// <param name="CompileItems">
/// The sources the project compiles when it lists them itself (absolute paths, document
/// order): a non-SDK project such as the ones Unity generates, or an SDK project with
/// default compile items switched off. Null when the project compiles its directory, so
/// the caller keeps enumerating it.
/// </param>
internal sealed record CsprojInfo(
    IReadOnlyList<string> ReferencePaths,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> DefineConstants,
    string? LangVersion,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<string>? CompileItems = null);

internal static class CsprojParser
{
    public static CsprojInfo? TryParse(string csprojPath)
    {
        if (!File.Exists(csprojPath)) return null;

        try
        {
            var doc = XDocument.Load(csprojPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var csprojDir = Path.GetDirectoryName(Path.GetFullPath(csprojPath)) ?? ".";

            var references = ExtractReferencePaths(doc, ns, csprojDir);
            var projectRefs = ExtractProjectReferences(doc, ns);
            var defines = ExtractDefineConstants(doc, ns);
            var langVersion = ExtractLangVersion(doc, ns);
            var targetFrameworks = ExtractTargetFrameworks(doc, ns);
            var compileItems = ExtractCompileItems(doc, ns, csprojDir);

            return new CsprojInfo(references, projectRefs, defines, langVersion, targetFrameworks, compileItems);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to parse {csprojPath}: {ex.Message}");
            return null;
        }
    }

    public static IReadOnlyList<string> DiscoverCsprojFiles(
        string projectRoot,
        IReadOnlyList<string>? excludeDirectories = null)
    {
        var results = new List<string>();
        var excludes = excludeDirectories ?? DefaultExcludes.ResolveProjectPaths(projectRoot);

        // Check for .sln and extract .csproj paths
        foreach (var sln in Directory.EnumerateFiles(projectRoot, "*.sln", SearchOption.TopDirectoryOnly))
        {
            results.AddRange(ExtractCsprojFromSln(sln, projectRoot)
                .Where(path => !DefaultExcludes.IsUnderExcludedDirectory(path, excludes)));
        }

        if (results.Count > 0) return results.Distinct().ToList();

        // Fallback: search for .csproj files directly
        try
        {
            results.AddRange(
                Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.AllDirectories)
                    .Where(path => !DefaultExcludes.IsUnderExcludedDirectory(path, excludes)));
        }
        catch (UnauthorizedAccessException) { }

        return results;
    }

    static List<string> ExtractReferencePaths(XDocument doc, XNamespace ns, string csprojDir)
    {
        var paths = new List<string>();

        foreach (var reference in doc.Descendants(ns + "Reference"))
        {
            var hintPath = reference.Element(ns + "HintPath")?.Value;
            if (hintPath is not null)
            {
                var fullPath = Path.GetFullPath(Path.Combine(csprojDir, hintPath));
                if (File.Exists(fullPath))
                    paths.Add(fullPath);
            }
        }

        return paths;
    }

    // Wildcards and MSBuild properties are not expanded: a project using them is treated as
    // directory-globbing, which is what the caller did for every project before.
    static List<string>? ExtractCompileItems(XDocument doc, XNamespace ns, string csprojDir)
    {
        if (UsesDefaultCompileItems(doc, ns))
            return null;

        var items = new List<string>();
        foreach (var compile in doc.Descendants(ns + "Compile"))
        {
            var include = compile.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
                continue;

            foreach (var item in include.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (item.Contains('*') || item.Contains('?') || item.Contains("$("))
                    return null;

                items.Add(Path.GetFullPath(Path.Combine(csprojDir, item.Replace('\\', Path.DirectorySeparatorChar))));
            }
        }

        return items.Count > 0 ? items : null;
    }

    // An SDK-style project compiles every .cs under its directory unless it opts out; an
    // explicit <Compile Include> there only adds to that glob and must not replace it.
    static bool UsesDefaultCompileItems(XDocument doc, XNamespace ns)
    {
        var root = doc.Root;
        if (root is null)
            return true;

        var isSdkStyle = root.Attribute("Sdk") is not null
            || root.Elements(ns + "Sdk").Any()
            || root.Elements(ns + "Import").Any(import => import.Attribute("Sdk") is not null);
        if (!isSdkStyle)
            return false;

        return !IsPropertyFalse(doc, ns, "EnableDefaultCompileItems")
            && !IsPropertyFalse(doc, ns, "EnableDefaultItems");
    }

    static bool IsPropertyFalse(XDocument doc, XNamespace ns, string propertyName) =>
        doc.Descendants(ns + propertyName)
            .Any(property => string.Equals(property.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));

    static List<string> ExtractProjectReferences(XDocument doc, XNamespace ns)
    {
        return doc.Descendants(ns + "ProjectReference")
            .Select(pr => pr.Attribute("Include")?.Value)
            .Where(v => v is not null)
            .Cast<string>()
            .ToList();
    }

    static List<string> ExtractDefineConstants(XDocument doc, XNamespace ns)
    {
        var defines = new List<string>();

        foreach (var prop in doc.Descendants(ns + "DefineConstants"))
        {
            var value = prop.Value;
            if (string.IsNullOrWhiteSpace(value)) continue;
            defines.AddRange(value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => d.Length > 0));
        }

        return defines.Distinct().ToList();
    }

    static string? ExtractLangVersion(XDocument doc, XNamespace ns)
    {
        return doc.Descendants(ns + "LangVersion").FirstOrDefault()?.Value;
    }

    static List<string> ExtractTargetFrameworks(XDocument doc, XNamespace ns)
    {
        var tfms = new List<string>();

        var multi = doc.Descendants(ns + "TargetFrameworks").FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(multi))
        {
            tfms.AddRange(multi.Split([';', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0));
        }

        var single = doc.Descendants(ns + "TargetFramework").FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(single))
            tfms.Insert(0, single.Trim());

        return tfms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static IEnumerable<string> ExtractCsprojFromSln(string slnPath, string projectRoot)
    {
        foreach (var line in File.ReadLines(slnPath))
        {
            if (!line.StartsWith("Project(")) continue;

            // Format: Project("{...}") = "Name", "Path.csproj", "{...}"
            var parts = line.Split('"');
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!parts[i].EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) continue;
                var relativePath = parts[i].Replace('\\', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(slnPath) ?? projectRoot, relativePath));
                if (File.Exists(fullPath))
                    yield return fullPath;
            }
        }
    }
}
