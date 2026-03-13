using System.Xml.Linq;

namespace Unilyze;

public sealed record CsprojInfo(
    IReadOnlyList<string> ReferencePaths,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> DefineConstants,
    string? LangVersion);

public static class CsprojParser
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

            return new CsprojInfo(references, projectRefs, defines, langVersion);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to parse {csprojPath}: {ex.Message}");
            return null;
        }
    }

    public static IReadOnlyList<string> DiscoverCsprojFiles(string projectRoot)
    {
        var results = new List<string>();

        // Check for .sln and extract .csproj paths
        foreach (var sln in Directory.EnumerateFiles(projectRoot, "*.sln", SearchOption.TopDirectoryOnly))
        {
            results.AddRange(ExtractCsprojFromSln(sln, projectRoot));
        }

        if (results.Count > 0) return results.Distinct().ToList();

        // Fallback: search for .csproj files directly
        try
        {
            results.AddRange(
                Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.AllDirectories)
                    .Where(p => !p.Contains("Library" + Path.DirectorySeparatorChar)));
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
