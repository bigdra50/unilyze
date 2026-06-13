using System.Text;
using Unilyze;

const string StartMarker = "<!-- docsgen:start -->";
const string EndMarker = "<!-- docsgen:end -->";

var check = args.Length == 1 && args[0] == "--check";
if (!check && args.Length != 0)
{
    Console.Error.WriteLine("usage: docsgen [--check]");
    return 2;
}

var repositoryRoot = ResolveRepositoryRoot();
var rulesDirectory = Path.Combine(repositoryRoot, "docs", "rules");
var definitions = SarifFormatter.EnumerateRuleDefinitions()
    .OrderBy(static definition => definition.RuleId, StringComparer.Ordinal)
    .ToArray();
var stalePaths = new List<string>();

foreach (var definition in definitions)
{
    var path = Path.Combine(rulesDirectory, $"{definition.RuleId}.md");
    UpdateOrCheck(path, RuleDocRenderer.Render(definition.RuleId), check, stalePaths);
}

var indexPath = Path.Combine(rulesDirectory, "index.md");
UpdateOrCheck(indexPath, RenderIndex(definitions), check, stalePaths);

if (stalePaths.Count == 0)
    return 0;

foreach (var stalePath in stalePaths)
    Console.Error.WriteLine($"stale or missing: {Path.GetRelativePath(repositoryRoot, stalePath)}");

return 1;

static string ResolveRepositoryRoot()
{
    DirectoryInfo? directory = new(Environment.CurrentDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "mkdocs.yml"))
            && File.Exists(Path.Combine(directory.FullName, "src", "Unilyze", "Unilyze.csproj")))
            return directory.FullName;

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Repository root not found.");
}

static void UpdateOrCheck(string path, string generatedBlock, bool check, List<string> stalePaths)
{
    var expected = BuildContent(path, generatedBlock);
    if (File.Exists(path) && Normalize(File.ReadAllText(path)) == Normalize(expected))
        return;

    if (check)
    {
        stalePaths.Add(path);
        return;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, expected, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

static string Normalize(string value) => value.Replace("\r\n", "\n");

static string BuildContent(string path, string generatedBlock)
{
    if (!File.Exists(path))
        return $"{StartMarker}{generatedBlock}{EndMarker}\n";

    var content = File.ReadAllText(path);
    var start = content.IndexOf(StartMarker, StringComparison.Ordinal);
    var end = content.IndexOf(EndMarker, StringComparison.Ordinal);
    if (start < 0 || end < start)
        throw new InvalidOperationException($"{path} must contain one {StartMarker}/{EndMarker} marker pair.");

    var blockStart = start + StartMarker.Length;
    return string.Concat(content.AsSpan(0, blockStart), generatedBlock, content.AsSpan(end));
}

static string RenderIndex(
    IReadOnlyList<(string RuleId, CodeSmellKind Kind, string ShortDescription)> definitions)
{
    var sb = new StringBuilder();
    sb.AppendLine();
    sb.AppendLine("# Rules");
    sb.AppendLine();
    sb.AppendLine("Unilyze reports the following SARIF rules.");
    sb.AppendLine();
    sb.AppendLine("| ID | Name | Severity entry points | Tags | Link |");
    sb.AppendLine("|----|------|-----------------------|------|------|");
    foreach (var definition in definitions)
    {
        var severity = RuleDocRenderer.GetSeverityEntryPoints(definition.RuleId);
        var tags = string.Join(", ", SmellThresholds.GetSarifTags(definition.Kind).Select(static tag => $"`{tag}`"));
        sb.AppendLine($"| {definition.RuleId} | {definition.ShortDescription} | {severity} | {tags} | [Details]({definition.RuleId}.md) |");
    }

    return sb.ToString().ReplaceLineEndings("\n");
}
