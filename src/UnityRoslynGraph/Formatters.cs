using System.Text;
using System.Web;

namespace UnityRoslynGraph;

public enum OutputFormat
{
    Csv,
    Mermaid,
    Dot,
    Drawio,
    Html
}

public static class Formatters
{
    // --- Assembly ---

    public static string AssemblyToCsv(IReadOnlyList<AsmdefInfo> asmdefs, string? prefix)
    {
        var sb = new StringBuilder();
        var filtered = Filter(asmdefs, prefix);
        var names = new HashSet<string>(filtered.Select(a => a.Name));

        foreach (var asm in filtered)
        {
            var label = Short(asm.Name, prefix);
            var refs = asm.References.Where(names.Contains).Select(r => Short(r, prefix));
            sb.AppendLine($"{label},{string.Join(";", refs)}");
        }

        return sb.ToString();
    }

    public static string AssemblyToMermaid(IReadOnlyList<AsmdefInfo> asmdefs, string? prefix)
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph BT");

        var filtered = Filter(asmdefs, prefix);
        var names = new HashSet<string>(filtered.Select(a => a.Name));

        foreach (var asm in filtered)
        {
            var from = Short(asm.Name, prefix);
            foreach (var r in asm.References.Where(names.Contains))
                sb.AppendLine($"  {from} --> {Short(r, prefix)}");
        }

        return sb.ToString();
    }

    public static string AssemblyToDot(IReadOnlyList<AsmdefInfo> asmdefs, string? prefix)
    {
        var sb = new StringBuilder();
        sb.AppendLine("digraph assemblies {");
        sb.AppendLine("  rankdir=BT;");

        var filtered = Filter(asmdefs, prefix);
        var names = new HashSet<string>(filtered.Select(a => a.Name));

        foreach (var asm in filtered)
        {
            var from = DotId(Short(asm.Name, prefix));
            foreach (var r in asm.References.Where(names.Contains))
                sb.AppendLine($"  {from} -> {DotId(Short(r, prefix))};");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    // --- Types ---

    public static string TypesToCsv(
        IReadOnlyList<TypeNodeInfo> types,
        IReadOnlyList<TypeDependency> deps,
        IReadOnlyList<TypeMetrics>? typeMetrics = null)
    {
        var sb = new StringBuilder();
        var metricsDict = (typeMetrics ?? []).ToDictionary(m => m.TypeName);

        sb.AppendLine("Name,Kind,Assembly,Members,Dependencies,CognitiveComplexity(avg),CognitiveComplexity(max),CodeHealth");

        foreach (var type in types)
        {
            var memberStr = string.Join(";", type.Members.Select(m => $"{m.MemberKind}:{m.Name}:{m.Type}"));
            var depStr = string.Join(";", deps.Where(d => d.FromType == type.Name).Select(d => $"{d.Kind}:{d.ToType}"));
            var ccAvg = "";
            var ccMax = "";
            var health = "";
            if (metricsDict.TryGetValue(type.Name, out var m))
            {
                ccAvg = m.AverageCognitiveComplexity.ToString("F1");
                ccMax = m.MaxCognitiveComplexity.ToString();
                health = m.CodeHealth.ToString("F1");
            }
            sb.AppendLine($"{type.Name},{type.Kind},{type.Assembly},{memberStr},{depStr},{ccAvg},{ccMax},{health}");
        }

        return sb.ToString();
    }

    public static string TypesToMermaid(IReadOnlyList<TypeNodeInfo> types, IReadOnlyList<TypeDependency> deps)
    {
        var sb = new StringBuilder();
        sb.AppendLine("classDiagram");

        foreach (var type in types)
        {
            var stereo = type.Kind switch
            {
                "interface" => "<<interface>>",
                "enum" => "<<enumeration>>",
                "record" or "record struct" => "<<record>>",
                "struct" => "<<struct>>",
                _ => ""
            };

            var safeName = MermaidSafe(type.Name);
            sb.AppendLine($"  class {safeName} {{");
            if (stereo.Length > 0)
                sb.AppendLine($"    {stereo}");

            foreach (var m in type.Members.Take(10))
            {
                var vis = m.MemberKind == "Method" ? "+" : (m.MemberKind == "EnumMember" ? "" : "+");
                sb.AppendLine($"    {vis}{m.Name} : {m.Type}");
            }

            sb.AppendLine("  }");
        }

        foreach (var dep in deps)
        {
            var arrow = dep.Kind switch
            {
                DependencyKind.Inheritance => " --|> ",
                DependencyKind.InterfaceImpl => " ..|> ",
                _ => " --> "
            };
            sb.AppendLine($"  {MermaidSafe(dep.FromType)}{arrow}{MermaidSafe(dep.ToType)}");
        }

        return sb.ToString();
    }

    public static string TypesToDot(IReadOnlyList<TypeNodeInfo> types, IReadOnlyList<TypeDependency> deps)
    {
        var sb = new StringBuilder();
        sb.AppendLine("digraph types {");
        sb.AppendLine("  rankdir=BT;");
        sb.AppendLine("  node [shape=record, fontname=\"Courier New\", fontsize=10];");

        foreach (var type in types)
        {
            var label = FormatDotLabel(type);
            sb.AppendLine($"  {DotId(type.Name)} [label=\"{label}\"];");
        }

        foreach (var dep in deps)
        {
            var style = dep.Kind switch
            {
                DependencyKind.Inheritance => " [arrowhead=empty]",
                DependencyKind.InterfaceImpl => " [arrowhead=empty, style=dashed]",
                _ => ""
            };
            sb.AppendLine($"  {DotId(dep.FromType)} -> {DotId(dep.ToType)}{style};");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    // --- Helpers ---

    static IReadOnlyList<AsmdefInfo> Filter(IReadOnlyList<AsmdefInfo> asmdefs, string? prefix) =>
        prefix != null
            ? asmdefs.Where(a => a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList()
            : asmdefs.ToList();

    static string Short(string name, string? prefix) =>
        prefix != null && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? name[prefix.Length..].TrimStart('.')
            : name;

    static string DotId(string name) =>
        name.Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_').Replace(' ', '_');

    static string MermaidSafe(string name) =>
        name.Replace('<', '~').Replace('>', '~').Replace(',', '_');

    static string FormatDotLabel(TypeNodeInfo type)
    {
        var sb = new StringBuilder();
        var stereo = type.Kind switch
        {
            "interface" => "\\<\\<interface\\>\\>\\n",
            "enum" => "\\<\\<enum\\>\\>\\n",
            "record" or "record struct" => "\\<\\<record\\>\\>\\n",
            _ => ""
        };

        sb.Append($"{{{stereo}{DotEsc(type.Name)}|");

        var members = type.Members.Take(10).ToList();
        foreach (var m in members)
        {
            var icon = m.MemberKind switch
            {
                "Field" => "- ",
                "Property" => "+ ",
                "Method" => "+ ",
                "EnumMember" => "  ",
                _ => "  "
            };
            sb.Append($"{DotEsc(icon + m.Name)}: {DotEsc(m.Type)}\\l");
        }

        if (type.Members.Count > 10)
            sb.Append($"... +{type.Members.Count - 10} more\\l");

        sb.Append('}');
        return sb.ToString();
    }

    static string DotEsc(string s) => s.Replace("\"", "\\\"").Replace("{", "\\{").Replace("}", "\\}").Replace("|", "\\|").Replace("<", "\\<").Replace(">", "\\>");
}
