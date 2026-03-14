using System.Text.Json;

namespace Unilyze;

public sealed record AsmdefInfo(
    string Name,
    string Directory,
    IReadOnlyList<string> References,
    IReadOnlyList<string>? UnresolvedReferences = null)
{
    public static IReadOnlyList<AsmdefInfo> Discover(string assetsDir)
    {
        var asmdefFiles = System.IO.Directory
            .EnumerateFiles(assetsDir, "*.asmdef", SearchOption.AllDirectories)
            .ToList();

        var guidToAssemblyName = BuildGuidLookup(asmdefFiles);
        var results = new List<AsmdefInfo>();

        foreach (var file in asmdefFiles)
        {
            var json = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.GetProperty("name").GetString() ?? Path.GetFileNameWithoutExtension(file);
            var (refs, unresolvedRefs) = ParseReferences(root, guidToAssemblyName);
            var dir = Path.GetDirectoryName(file) ?? "";
            results.Add(new AsmdefInfo(name, dir, refs, unresolvedRefs.Count > 0 ? unresolvedRefs : null));
        }

        return results;
    }

    static (List<string> References, List<string> UnresolvedReferences) ParseReferences(
        JsonElement root,
        IReadOnlyDictionary<string, string> guidToAssemblyName)
    {
        var refs = new List<string>();
        var unresolvedRefs = new List<string>();
        if (!root.TryGetProperty("references", out var refsEl))
            return (refs, unresolvedRefs);

        foreach (var r in refsEl.EnumerateArray())
        {
            var refName = r.GetString();
            if (refName is null)
                continue;

            if (refName.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase))
            {
                var guid = refName["GUID:".Length..];
                if (guidToAssemblyName.TryGetValue(guid, out var resolvedName))
                    refs.Add(resolvedName);
                else
                    unresolvedRefs.Add(refName);
                continue;
            }

            refs.Add(refName);
        }

        return (refs, unresolvedRefs);
    }

    static Dictionary<string, string> BuildGuidLookup(IReadOnlyList<string> asmdefFiles)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in asmdefFiles)
        {
            var guid = TryReadGuid(file);
            if (string.IsNullOrWhiteSpace(guid))
                continue;

            var name = TryReadAssemblyName(file) ?? Path.GetFileNameWithoutExtension(file);
            lookup.TryAdd(guid, name);
        }

        return lookup;
    }

    static string? TryReadAssemblyName(string asmdefFile)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(asmdefFile));
        return doc.RootElement.TryGetProperty("name", out var nameProperty)
            ? nameProperty.GetString()
            : null;
    }

    static string? TryReadGuid(string asmdefFile)
    {
        var metaFile = asmdefFile + ".meta";
        if (!File.Exists(metaFile))
            return null;

        foreach (var line in File.ReadLines(metaFile))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
                continue;

            var guid = trimmed["guid:".Length..].Trim();
            return string.IsNullOrEmpty(guid) ? null : guid;
        }

        return null;
    }
}
