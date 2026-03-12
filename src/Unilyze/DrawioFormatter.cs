using System.Text;
using System.Web;
using System.Xml.Linq;

namespace Unilyze;

public static class DrawioFormatter
{
    const int ColumnCount = 3;
    const int NodeSpacingX = 40;
    const int NodeSpacingY = 40;
    const int StartX = 40;
    const int StartY = 40;
    const int MemberLineHeight = 18;
    const int HeaderHeight = 30;
    const int MinBoxWidth = 220;
    const int MaxMembers = 12;
    const int CharWidth = 7;

    static int _edgeCounter;
    static Dictionary<string, TypeMetrics> _typeMetricsLookup = new();

    // --- Public API ---

    public static string AssemblyToDrawio(
        IReadOnlyList<AsmdefInfo> asmdefs,
        IReadOnlyList<AssemblyMetrics>? metrics,
        string? prefix,
        IReadOnlyDictionary<string, AssemblyHealthMetrics>? healthMetrics = null)
    {
        _edgeCounter = 0;
        _typeMetricsLookup = new();
        var page = BuildAssemblyOverviewPage(asmdefs, metrics ?? [], prefix, healthMetrics);
        return WrapMxfile([page]);
    }

    public static string TypesToDrawio(
        IReadOnlyList<TypeNodeInfo> types,
        IReadOnlyList<TypeDependency> deps,
        IReadOnlyList<TypeMetrics>? typeMetrics = null)
    {
        _edgeCounter = 0;
        _typeMetricsLookup = (typeMetrics ?? []).ToDictionary(m => m.TypeName);
        var page = BuildTypePage("Types", "tp0", types, deps);
        return WrapMxfile([page]);
    }

    public static string GenerateMultiPage(
        IReadOnlyList<AsmdefInfo> asmdefs,
        IReadOnlyList<AssemblyMetrics> metrics,
        Dictionary<string, IReadOnlyList<TypeNodeInfo>> typesByAssembly,
        IReadOnlyList<TypeDependency> allDeps,
        string? prefix,
        IReadOnlyDictionary<string, AssemblyHealthMetrics>? healthMetrics = null,
        IReadOnlyList<TypeMetrics>? typeMetrics = null)
    {
        _edgeCounter = 0;
        _typeMetricsLookup = (typeMetrics ?? []).ToDictionary(m => m.TypeName);
        var pages = new List<XElement>();

        // Page 1: Assembly Overview
        pages.Add(BuildAssemblyOverviewPage(asmdefs, metrics, prefix, healthMetrics));

        // Page 2-N: Types per Assembly
        var pageIdx = 1;
        foreach (var (asmName, types) in typesByAssembly.OrderBy(kv => kv.Key))
        {
            var asmDeps = FilterDepsForTypes(types, allDeps);
            var shortName = prefix != null && asmName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? asmName[prefix.Length..].TrimStart('.')
                : asmName;
            if (string.IsNullOrEmpty(shortName)) shortName = asmName;
            pages.Add(BuildTypePage($"Types: {shortName}", $"tp{pageIdx}", types, asmDeps));
            pageIdx++;
        }

        // Page N+1: Cross-Assembly Deps
        pages.Add(BuildCrossAssemblyDepsPage(typesByAssembly, allDeps, prefix));

        // Page N+2: Interface Map
        pages.Add(BuildInterfaceMapPage(typesByAssembly, allDeps));

        return WrapMxfile(pages);
    }

    // --- Page Builders ---

    static XElement BuildAssemblyOverviewPage(
        IReadOnlyList<AsmdefInfo> asmdefs,
        IReadOnlyList<AssemblyMetrics> metrics,
        string? prefix,
        IReadOnlyDictionary<string, AssemblyHealthMetrics>? healthMetrics = null)
    {
        var filtered = prefix != null
            ? asmdefs.Where(a => a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList()
            : asmdefs.ToList();

        var names = new HashSet<string>(filtered.Select(a => a.Name));
        var metricsDict = metrics.ToDictionary(m => m.AssemblyName);
        var healthDict = healthMetrics ?? new Dictionary<string, AssemblyHealthMetrics>();

        var cells = new List<XElement>();
        var col = 0;
        var y = StartY;
        var maxHeightInRow = 0;

        foreach (var asm in filtered)
        {
            var label = prefix != null ? asm.Name[prefix.Length..].TrimStart('.') : asm.Name;
            if (string.IsNullOrEmpty(label)) label = asm.Name;

            var metricsHtml = "";
            if (metricsDict.TryGetValue(asm.Name, out var m))
            {
                metricsHtml = "<hr size=\"1\"><font style=\"font-size:10px\">" +
                    $"Types: {m.TypeCount} (C:{m.ClassCount} R:{m.RecordCount} I:{m.InterfaceCount} E:{m.EnumCount} D:{m.DelegateCount})<br>" +
                    $"Public: {m.PublicTypeCount} | Sealed: {m.SealedTypeCount} | Members: {m.TotalMembers}";
                if (healthDict.TryGetValue(asm.Name, out var hm))
                    metricsHtml += $"<br>Health: {hm.AverageCodeHealth:F1} (min {hm.MinCodeHealth:F1}) | Avg CC: {hm.AverageCognitiveComplexity:F1}";
                metricsHtml += "</font>";
            }

            var htmlLabel = $"<b>{Esc(label)}</b>{metricsHtml}";
            var width = Math.Max(280, label.Length * 10 + 40);
            var height = metricsHtml.Length > 0 ? 80 : 40;

            var x = StartX + col * (width + NodeSpacingX);
            var cellId = $"asm-{SafeId(asm.Name)}";

            var (fill, stroke) = LayerColorPair(label);
            cells.Add(CreateCell(cellId, htmlLabel,
                $"rounded=1;whiteSpace=wrap;html=1;fillColor={fill};strokeColor={stroke};align=center;verticalAlign=top;spacingTop=4;fontSize=12;",
                x, y, width, height));

            maxHeightInRow = Math.Max(maxHeightInRow, height);
            col++;
            if (col >= ColumnCount)
            {
                col = 0;
                y += maxHeightInRow + NodeSpacingY;
                maxHeightInRow = 0;
            }
        }

        // Edges
        foreach (var asm in filtered)
        {
            foreach (var r in asm.References)
            {
                if (!names.Contains(r)) continue;
                var fromId = $"asm-{SafeId(asm.Name)}";
                var toId = $"asm-{SafeId(r)}";
                cells.Add(CreateEdge(NextEdgeId(), fromId, toId,
                    "endArrow=open;endFill=0;strokeColor=#666666;"));
            }
        }

        return WrapPage("asm-overview", "Assembly Overview", cells);
    }

    static XElement BuildTypePage(
        string pageName,
        string pagePrefix,
        IReadOnlyList<TypeNodeInfo> types,
        IReadOnlyList<TypeDependency> deps)
    {
        var cells = new List<XElement>();
        var sorted = types.OrderBy(t => TypeSortKey(t.Kind)).ThenBy(t => t.Name).ToList();

        // First pass: compute sizes and max column widths
        var sizes = sorted.Select(ComputeTypeBoxSize).ToList();
        var maxColWidths = new int[ColumnCount];
        for (var i = 0; i < sizes.Count; i++)
            maxColWidths[i % ColumnCount] = Math.Max(maxColWidths[i % ColumnCount], sizes[i].width);

        // Second pass: layout
        var positions = new Dictionary<string, string>();
        var col = 0;
        var y = StartY;
        var maxHeightInRow = 0;

        for (var i = 0; i < sorted.Count; i++)
        {
            var type = sorted[i];
            var (w, h) = sizes[i];
            var cellId = $"{pagePrefix}-{SafeId(type.Name)}";
            positions[type.Name] = cellId;

            var x = StartX;
            for (var c = 0; c < col; c++)
                x += maxColWidths[c] + NodeSpacingX;

            cells.AddRange(CreateTypeBox(cellId, type, x, y, w, h));

            maxHeightInRow = Math.Max(maxHeightInRow, h);
            col++;
            if (col >= ColumnCount)
            {
                col = 0;
                y += maxHeightInRow + NodeSpacingY;
                maxHeightInRow = 0;
            }
        }

        // Edges
        foreach (var dep in deps)
        {
            if (!positions.TryGetValue(dep.FromType, out var fromId)) continue;
            if (!positions.TryGetValue(dep.ToType, out var toId)) continue;
            cells.Add(CreateEdge(NextEdgeId(), fromId, toId, DependencyEdgeStyle(dep.Kind)));
        }

        return WrapPage(pagePrefix, pageName, cells);
    }

    static XElement BuildCrossAssemblyDepsPage(
        Dictionary<string, IReadOnlyList<TypeNodeInfo>> typesByAssembly,
        IReadOnlyList<TypeDependency> allDeps,
        string? prefix)
    {
        var typeToAssembly = new Dictionary<string, string>();
        foreach (var (asmName, types) in typesByAssembly)
            foreach (var t in types)
                typeToAssembly[t.Name] = asmName;

        var crossDeps = allDeps
            .Where(d => typeToAssembly.ContainsKey(d.FromType) && typeToAssembly.ContainsKey(d.ToType)
                        && typeToAssembly[d.FromType] != typeToAssembly[d.ToType])
            .ToList();

        var involvedTypes = new HashSet<string>(
            crossDeps.SelectMany(d => new[] { d.FromType, d.ToType }));

        var cells = new List<XElement>();
        var positions = new Dictionary<string, string>();
        var groupX = StartX;

        foreach (var (asmName, types) in typesByAssembly.OrderBy(kv => kv.Key))
        {
            var relevantTypes = types.Where(t => involvedTypes.Contains(t.Name))
                .OrderBy(t => t.Name).ToList();
            if (relevantTypes.Count == 0) continue;

            var shortName = prefix != null && asmName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? asmName[prefix.Length..].TrimStart('.')
                : asmName;
            if (string.IsNullOrEmpty(shortName)) shortName = asmName;

            var maxWidth = MinBoxWidth;
            foreach (var t in relevantTypes)
            {
                var (w, _) = ComputeTypeBoxSize(t);
                maxWidth = Math.Max(maxWidth, w);
            }

            // Assembly label
            cells.Add(CreateCell($"xd-lbl-{SafeId(asmName)}", $"<b>{Esc(shortName)}</b>",
                "text;html=1;align=center;verticalAlign=middle;resizable=0;autosize=1;strokeColor=none;fillColor=none;fontSize=14;fontStyle=1;",
                groupX, StartY, maxWidth, 30));

            var y = StartY + 40;
            foreach (var type in relevantTypes)
            {
                var (w, h) = ComputeTypeBoxSize(type);
                var cellId = $"xd-{SafeId(asmName)}-{SafeId(type.Name)}";
                positions[type.Name] = cellId;
                cells.AddRange(CreateTypeBox(cellId, type, groupX, y, w, h));
                y += h + NodeSpacingY;
            }

            groupX += maxWidth + NodeSpacingX + 40;
        }

        // Cross-assembly edges
        foreach (var dep in crossDeps)
        {
            if (!positions.TryGetValue(dep.FromType, out var fromId)) continue;
            if (!positions.TryGetValue(dep.ToType, out var toId)) continue;
            cells.Add(CreateEdge(NextEdgeId(), fromId, toId, DependencyEdgeStyle(dep.Kind)));
        }

        return WrapPage("xdeps", "Cross-Assembly Deps", cells);
    }

    static XElement BuildInterfaceMapPage(
        Dictionary<string, IReadOnlyList<TypeNodeInfo>> typesByAssembly,
        IReadOnlyList<TypeDependency> allDeps)
    {
        var implDeps = allDeps.Where(d => d.Kind == DependencyKind.InterfaceImpl).ToList();
        if (implDeps.Count == 0)
            return WrapPage("imap", "Interface Map", []);

        var involvedTypes = new HashSet<string>(
            implDeps.SelectMany(d => new[] { d.FromType, d.ToType }));

        var allTypesList = typesByAssembly.Values.SelectMany(t => t).ToList();
        var interfaces = allTypesList
            .Where(t => t.Kind == "interface" && involvedTypes.Contains(t.Name))
            .OrderBy(t => t.Assembly).ThenBy(t => t.Name).ToList();
        var implementors = allTypesList
            .Where(t => t.Kind != "interface" && involvedTypes.Contains(t.Name))
            .OrderBy(t => t.Assembly).ThenBy(t => t.Name).ToList();

        var cells = new List<XElement>();
        var positions = new Dictionary<string, string>();

        // Interfaces on the left
        var ifaceY = StartY;
        foreach (var iface in interfaces)
        {
            var (w, h) = ComputeTypeBoxSize(iface);
            var cellId = $"im-{SafeId(iface.Assembly)}-{SafeId(iface.Name)}";
            positions[iface.Name] = cellId;
            cells.AddRange(CreateTypeBox(cellId, iface, StartX, ifaceY, w, h));
            ifaceY += h + NodeSpacingY;
        }

        // Implementors on the right
        var implX = StartX + MinBoxWidth + 200;
        var implY = StartY;
        foreach (var impl in implementors)
        {
            var (w, h) = ComputeTypeBoxSize(impl);
            var cellId = $"im-{SafeId(impl.Assembly)}-{SafeId(impl.Name)}";
            positions[impl.Name] = cellId;
            cells.AddRange(CreateTypeBox(cellId, impl, implX, implY, w, h));
            implY += h + NodeSpacingY;
        }

        // Implementation edges
        foreach (var dep in implDeps)
        {
            if (!positions.TryGetValue(dep.FromType, out var fromId)) continue;
            if (!positions.TryGetValue(dep.ToType, out var toId)) continue;
            cells.Add(CreateEdge(NextEdgeId(), fromId, toId,
                "dashed=1;endArrow=block;endFill=0;endSize=12;strokeColor=#82B366;"));
        }

        return WrapPage("imap", "Interface Map", cells);
    }

    // --- Cell/Edge Builders ---

    static XElement CreateCell(string id, string htmlValue, string style,
        int x, int y, int width, int height, string parent = "1")
    {
        return new XElement("mxCell",
            new XAttribute("id", id),
            new XAttribute("value", htmlValue),
            new XAttribute("style", style),
            new XAttribute("vertex", "1"),
            new XAttribute("parent", parent),
            new XElement("mxGeometry",
                new XAttribute("x", x),
                new XAttribute("y", y),
                new XAttribute("width", width),
                new XAttribute("height", height),
                new XAttribute("as", "geometry")));
    }

    static IEnumerable<XElement> CreateTypeBox(
        string cellId, TypeNodeInfo type, int x, int y, int width, int height, string parent = "1")
    {
        _typeMetricsLookup.TryGetValue(type.Name, out var metrics);
        var (fill, stroke) = KindColorPair(type.Kind);
        if (metrics != null)
        {
            var healthStroke = metrics.CodeHealth switch
            {
                >= 9.0 => "#56D364",
                >= 7.0 => "#7EE787",
                >= 4.0 => "#D6B656",
                _ => "#B85450"
            };
            stroke = healthStroke;
        }

        var stereo = type.Kind switch
        {
            "interface" => "«interface» ",
            "enum" => "«enum» ",
            "record" or "record struct" => "«record» ",
            "struct" => "«struct» ",
            _ => ""
        };

        var healthBadge = metrics != null ? $" [{metrics.CodeHealth:F1}]" : "";
        var headerHtml = $"<b>{Esc(stereo)}{Esc(type.Name)}{healthBadge}</b>";

        // Container (swimlane)
        yield return new XElement("mxCell",
            new XAttribute("id", cellId),
            new XAttribute("value", headerHtml),
            new XAttribute("style",
                $"swimlane;fontStyle=0;align=center;startSize={HeaderHeight};html=1;" +
                $"fillColor={fill};strokeColor={stroke};collapsible=0;"),
            new XAttribute("vertex", "1"),
            new XAttribute("parent", parent),
            new XElement("mxGeometry",
                new XAttribute("x", x),
                new XAttribute("y", y),
                new XAttribute("width", width),
                new XAttribute("height", height),
                new XAttribute("as", "geometry")));

        // Members section
        var membersHtml = FormatMembersHtml(type);
        yield return new XElement("mxCell",
            new XAttribute("id", $"{cellId}-m"),
            new XAttribute("value", membersHtml),
            new XAttribute("style",
                "text;html=1;strokeColor=none;fillColor=none;align=left;verticalAlign=top;" +
                "spacingLeft=4;spacingRight=4;overflow=hidden;rotatable=0;" +
                "points=[[0,0.5],[1,0.5]];portConstraint=eastwest;" +
                "fontFamily=Courier New;fontSize=10;"),
            new XAttribute("vertex", "1"),
            new XAttribute("parent", cellId),
            new XElement("mxGeometry",
                new XAttribute("y", HeaderHeight),
                new XAttribute("width", width),
                new XAttribute("height", height - HeaderHeight),
                new XAttribute("as", "geometry")));
    }

    static XElement CreateEdge(string id, string sourceId, string targetId, string style)
    {
        return new XElement("mxCell",
            new XAttribute("id", id),
            new XAttribute("value", ""),
            new XAttribute("style", style),
            new XAttribute("edge", "1"),
            new XAttribute("source", sourceId),
            new XAttribute("target", targetId),
            new XAttribute("parent", "1"),
            new XElement("mxGeometry",
                new XAttribute("relative", "1"),
                new XAttribute("as", "geometry")));
    }

    // --- XML Helpers ---

    static string WrapMxfile(IReadOnlyList<XElement> pages)
    {
        var mxfile = new XElement("mxfile",
            new XAttribute("host", "unilyze"),
            new XAttribute("type", "device"),
            pages);

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), mxfile);
        using var sw = new Utf8StringWriter();
        doc.Save(sw);
        return sw.ToString();
    }

    static XElement WrapPage(string id, string name, IReadOnlyList<XElement> cells)
    {
        var rootId = $"{id}-0";
        var layerId = $"{id}-1";

        var root = new XElement("root",
            new XElement("mxCell", new XAttribute("id", rootId)),
            new XElement("mxCell", new XAttribute("id", layerId), new XAttribute("parent", rootId)));

        foreach (var cell in cells)
        {
            // Rewrite generic parent="1" to page-scoped layer ID
            var parentAttr = cell.Attribute("parent");
            if (parentAttr?.Value == "1")
                parentAttr.Value = layerId;
            root.Add(cell);
        }

        return new XElement("diagram",
            new XAttribute("id", id),
            new XAttribute("name", name),
            new XAttribute("adaptiveColors", "auto"),
            new XElement("mxGraphModel",
                new XAttribute("dx", "1422"),
                new XAttribute("dy", "762"),
                new XAttribute("grid", "1"),
                new XAttribute("gridSize", "10"),
                new XAttribute("guides", "1"),
                new XAttribute("tooltips", "1"),
                new XAttribute("connect", "1"),
                new XAttribute("arrows", "1"),
                new XAttribute("fold", "1"),
                new XAttribute("page", "1"),
                new XAttribute("pageScale", "1"),
                new XAttribute("pageWidth", "1169"),
                new XAttribute("pageHeight", "827"),
                new XAttribute("adaptiveColors", "auto"),
                root));
    }

    // --- Sizing ---

    static (int width, int height) ComputeTypeBoxSize(TypeNodeInfo type)
    {
        var memberCount = Math.Min(type.Members.Count, MaxMembers);
        var extraLines = 0;
        if (type.Modifiers.Count > 0) extraLines++;
        if (type.Attributes.Count > 0) extraLines++;

        var bodyHeight = extraLines * 14 + memberCount * MemberLineHeight + 12;
        var height = HeaderHeight + Math.Max(bodyHeight, MemberLineHeight);

        var maxLen = type.Name.Length + 14;
        foreach (var m in type.Members.Take(MaxMembers))
        {
            var line = FormatMemberLine(m);
            maxLen = Math.Max(maxLen, line.Length);
        }
        var width = Math.Max(MinBoxWidth, maxLen * CharWidth + 20);

        return (width, height);
    }

    // --- Formatting ---

    static string FormatMembersHtml(TypeNodeInfo type)
    {
        var sb = new StringBuilder();

        if (type.Modifiers.Count > 0)
            sb.Append($"<font color=\"#888\" style=\"font-size:9px\">{Esc(string.Join(" ", type.Modifiers))}</font><br>");
        if (type.Attributes.Count > 0)
            sb.Append($"<font color=\"#6A9955\" style=\"font-size:9px\">[{Esc(string.Join(", ", type.Attributes.Select(a => a.Name)))}]</font><br>");

        var displayed = type.Members.Take(MaxMembers).ToList();
        foreach (var m in displayed)
        {
            sb.Append(Esc(FormatMemberLine(m)));
            sb.Append("<br>");
        }

        if (type.Members.Count > MaxMembers)
            sb.Append($"<font color=\"#888\">... +{type.Members.Count - MaxMembers} more</font>");

        return sb.ToString();
    }

    static string FormatMemberLine(MemberInfo m)
    {
        var vis = m.Modifiers.Contains("private") ? "- "
            : m.Modifiers.Contains("protected") ? "# "
            : "+ ";

        if (m.MemberKind == "EnumMember")
            return m.Name;

        if (m.MemberKind == "Method")
        {
            var paramStr = string.Join(", ", m.Parameters.Select(p => $"{p.Name}: {p.Type}"));
            return $"{vis}{m.Name}({paramStr}) : {m.Type}";
        }

        return $"{vis}{m.Name} : {m.Type}";
    }

    // --- Style Helpers ---

    static string DependencyEdgeStyle(DependencyKind kind) => kind switch
    {
        DependencyKind.Inheritance => "endArrow=block;endFill=0;endSize=12;strokeColor=#6C8EBF;",
        DependencyKind.InterfaceImpl => "dashed=1;endArrow=block;endFill=0;endSize=12;strokeColor=#82B366;",
        DependencyKind.ConstructorParam => "dashed=1;endArrow=open;endFill=0;strokeColor=#D6B656;",
        DependencyKind.MethodParam => "dashed=1;endArrow=open;endFill=0;strokeColor=#999999;",
        _ => "endArrow=open;endFill=0;strokeColor=#666666;"
    };

    static int TypeSortKey(string kind) => kind switch
    {
        "interface" => 0,
        "enum" => 1,
        "record" or "record struct" => 2,
        "struct" => 3,
        "class" => 4,
        _ => 5
    };

    static string SafeId(string name) =>
        name.Replace('<', '_').Replace('>', '_').Replace(',', '_').Replace(' ', '_').Replace('.', '_');

    static string Esc(string s) => HttpUtility.HtmlEncode(s);

    static string NextEdgeId() => $"e{++_edgeCounter}";

    static (string fill, string stroke) KindColorPair(string kind) => kind switch
    {
        "interface" => ("#E8F5E9", "#82B366"),
        "enum" => ("#FFF9C4", "#D6B656"),
        "record" or "record struct" => ("#E3F2FD", "#6C8EBF"),
        "struct" => ("#FFF3E0", "#D6B656"),
        "class" => ("#F3E5F5", "#9673A6"),
        _ => ("#FFFFFF", "#999999")
    };

    static (string fill, string stroke) LayerColorPair(string label) => label.ToLowerInvariant() switch
    {
        "domain" => ("#E3F2FD", "#6C8EBF"),
        "application" => ("#E8F5E9", "#82B366"),
        var s when s.Contains("infrastructure") => ("#FFF3E0", "#D6B656"),
        "presentation" => ("#F3E5F5", "#9673A6"),
        "composition" => ("#ECEFF1", "#999999"),
        "editor" => ("#FFF9C4", "#D6B656"),
        _ => ("#FFFFFF", "#999999")
    };

    static IReadOnlyList<TypeDependency> FilterDepsForTypes(
        IReadOnlyList<TypeNodeInfo> types,
        IReadOnlyList<TypeDependency> allDeps)
    {
        var typeNames = new HashSet<string>(types.Select(t => t.Name));
        return allDeps.Where(d => typeNames.Contains(d.FromType) && typeNames.Contains(d.ToType)).ToList();
    }

    sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
