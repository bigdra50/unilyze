using System.Text.Json;

namespace Unilyze;

public sealed record AsmdefInfo(string Name, string Directory, IReadOnlyList<string> References)
{
    public static IReadOnlyList<AsmdefInfo> Discover(string assetsDir)
    {
        var results = new List<AsmdefInfo>();

        foreach (var file in System.IO.Directory.EnumerateFiles(assetsDir, "*.asmdef", SearchOption.AllDirectories))
        {
            var json = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.GetProperty("name").GetString() ?? Path.GetFileNameWithoutExtension(file);
            var refs = ParseReferences(root);
            var dir = Path.GetDirectoryName(file) ?? "";
            results.Add(new AsmdefInfo(name, dir, refs));
        }

        return results;
    }

    static List<string> ParseReferences(JsonElement root)
    {
        var refs = new List<string>();
        if (!root.TryGetProperty("references", out var refsEl))
            return refs;

        foreach (var r in refsEl.EnumerateArray())
        {
            var refName = r.GetString();
            if (refName is null || refName.StartsWith("GUID:"))
                continue;
            refs.Add(refName);
        }

        return refs;
    }
}
