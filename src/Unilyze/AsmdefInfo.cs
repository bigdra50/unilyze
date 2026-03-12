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
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.GetProperty("name").GetString() ?? Path.GetFileNameWithoutExtension(file);
            var refs = new List<string>();

            if (root.TryGetProperty("references", out var refsEl))
            {
                foreach (var r in refsEl.EnumerateArray())
                {
                    var refName = r.GetString();
                    if (refName != null)
                    {
                        // Strip GUID: prefix if present
                        if (refName.StartsWith("GUID:"))
                            continue;
                        refs.Add(refName);
                    }
                }
            }

            var dir = Path.GetDirectoryName(file) ?? "";
            results.Add(new AsmdefInfo(name, dir, refs));
        }

        return results;
    }
}
