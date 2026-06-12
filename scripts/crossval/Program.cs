// Cross-validation harness for issue #4: re-measures cyclomatic complexity with the
// official Roslyn metrics engine (CodeAnalysisMetricData, the implementation behind
// Metrics.exe) and emits per-type / per-member data plus per-method counts of the
// constructs unilyze counts beyond CA1502 (?. / ?? / ??= / switch expression arms).
//
// Usage:
//   dotnet run --project scripts/crossval -- <path-to-Unilyze.csproj> > official-cc.json
//   dotnet run --project scripts/crossval -- compare <official-cc.json> <unilyze-cc.json>

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeMetrics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

if (args.Length >= 1 && args[0] == "compare")
{
    if (args.Length != 3)
    {
        Console.Error.WriteLine("usage: crossval compare <official-cc.json> <unilyze-cc.json>");
        Environment.Exit(2);
    }

    Environment.Exit(await CompareMode.RunAsync(args[1], args[2]));
    return;
}

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: crossval <path-to-Unilyze.csproj>");
    Console.Error.WriteLine("       crossval compare <official-cc.json> <unilyze-cc.json>");
    Environment.Exit(2);
}

MSBuildLocator.RegisterDefaults();

var projectPath = Path.GetFullPath(args[0]);

using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
{
    ["TargetFramework"] = "net10.0",
});
workspace.WorkspaceFailed += (_, e) =>
{
    if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
        Console.Error.WriteLine($"workspace: {e.Diagnostic.Message}");
};

var project = await workspace.OpenProjectAsync(projectPath);
var compilation = await project.GetCompilationAsync()
    ?? throw new InvalidOperationException("compilation unavailable");

var context = new CodeMetricsAnalysisContext(compilation, CancellationToken.None);
var assemblyData = await CodeAnalysisMetricData.ComputeAsync(compilation.Assembly, context);

var types = new List<TypeReport>();
Walk(assemblyData);

void Walk(CodeAnalysisMetricData data)
{
    if (data.Symbol is INamedTypeSymbol type &&
        type.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface)
    {
        var members = data.Children
            .Select(c => new MemberReport(
                c.Symbol.Kind.ToString(),
                c.Symbol.MetadataName,
                c.CyclomaticComplexity))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        types.Add(new TypeReport(
            type.MetadataName,
            data.CyclomaticComplexity,
            members,
            CountExtendedConstructs(type),
            type.ContainingType?.MetadataName));
    }

    foreach (var child in data.Children)
        Walk(child);
}

List<MethodConstructs> CountExtendedConstructs(INamedTypeSymbol type)
{
    var reports = new List<MethodConstructs>();
    foreach (var syntaxRef in type.DeclaringSyntaxReferences)
    {
        if (syntaxRef.GetSyntax() is not TypeDeclarationSyntax decl)
            continue;

        var model = compilation.GetSemanticModel(decl.SyntaxTree);

        foreach (var method in decl.Members.OfType<MethodDeclarationSyntax>())
        {
            var nodes = method.DescendantNodes(n => n is not TypeDeclarationSyntax).ToList();
            var shared = nodes.Count(n => n is IfStatementSyntax or ConditionalExpressionSyntax
                    or ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax
                    or ConditionalAccessExpressionSyntax or CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax)
                + nodes.Count(n => n.IsKind(SyntaxKind.LogicalAndExpression)
                    || n.IsKind(SyntaxKind.LogicalOrExpression)
                    || n.IsKind(SyntaxKind.CoalesceExpression));

            reports.Add(new MethodConstructs(
                method.Identifier.Text,
                Shared: shared,
                DeconstructForeach: nodes.Count(n => n is ForEachVariableStatementSyntax),
                DefaultLabels: nodes.Count(n => n is DefaultSwitchLabelSyntax),
                Catches: nodes.Count(n => n is CatchClauseSyntax),
                SwitchArms: nodes.Count(n => n is SwitchExpressionArmSyntax),
                Gotos: nodes.Count(n => n is GotoStatementSyntax),
                BoolAmpOr: nodes.Count(n => n is BinaryExpressionSyntax binary
                    && (binary.IsKind(SyntaxKind.BitwiseAndExpression) || binary.IsKind(SyntaxKind.BitwiseOrExpression))
                    && IsBooleanType(binary.Left, model))));
        }
    }

    return reports;
}

static bool IsBooleanType(ExpressionSyntax expression, SemanticModel model) =>
    model.GetTypeInfo(expression).Type?.SpecialType == SpecialType.System_Boolean;

var report = types
    .Where(t => !t.Name.StartsWith("<", StringComparison.Ordinal))
    .OrderBy(t => t.Name, StringComparer.Ordinal)
    .ToList();

Console.WriteLine(JsonSerializer.Serialize(report, CrossValJsonContext.Default.ListTypeReport));

internal sealed record MethodConstructs(
    string Name,
    int Shared,
    int DeconstructForeach,
    int DefaultLabels,
    int Catches,
    int SwitchArms,
    int Gotos,
    int BoolAmpOr);

internal sealed record MemberReport(string Kind, string Name, int CycCC);

internal sealed record TypeReport(
    string Name,
    int OfficialCycCC,
    List<MemberReport> Members,
    List<MethodConstructs> Methods,
    string? ContainingType = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<TypeReport>))]
[JsonSerializable(typeof(UnilyzeAnalysisDocument))]
internal partial class CrossValJsonContext : JsonSerializerContext;

static partial class CompareMode
{
    // Source-generated partials: official engine sees generated members, unilyze does not.
    // Program: top-level statements compile to Program.<Main>$; unilyze has no matching type.
    static readonly HashSet<string> ExcludedOfficialTypes = new(StringComparer.Ordinal)
    {
        "Program",
    };

    // docs/metrics.md:347 — SyntaxOnly bool `&`|` resolution and nested-type member matching.
    // Keys are container-qualified so an unrelated future type named State/Walker never inherits the tolerance.
    static readonly Dictionary<string, string> ResidualAllowlist = new(StringComparer.Ordinal)
    {
        ["HalsteadCalculator.HalsteadWalker"] =
            "SyntaxOnly cannot resolve bool `&`/`|` operand types; nested HalsteadWalker ctor base differs by 1.",
        ["CognitiveComplexity.State"] =
            "Nested CognitiveComplexity.State: implicit ctor base and member-name matching differ by 1.",
        ["CyclomaticComplexity.Walker"] =
            "Nested CyclomaticComplexity.Walker: SyntaxOnly bool `&`/`|` and nested member matching differ by 1.",
    };

    public static async Task<int> RunAsync(string officialPath, string unilyzePath)
    {
        var officialJson = await File.ReadAllTextAsync(officialPath);
        var unilyzeJson = await File.ReadAllTextAsync(unilyzePath);

        var official = JsonSerializer.Deserialize(officialJson, CrossValJsonContext.Default.ListTypeReport)
            ?? throw new InvalidOperationException($"failed to deserialize {officialPath}");
        var unilyze = JsonSerializer.Deserialize(unilyzeJson, CrossValJsonContext.Default.UnilyzeAnalysisDocument)
            ?? throw new InvalidOperationException($"failed to deserialize {unilyzePath}");

        var unilyzeByName = unilyze.Types
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var unexplained = new List<string>();
        var compared = 0;

        foreach (var type in official.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            if (type.Name.StartsWith("<", StringComparison.Ordinal)
                || type.Name.EndsWith("JsonContext", StringComparison.Ordinal)
                || ExcludedOfficialTypes.Contains(type.Name))
                continue;

            var unilyzeName = ToUnilyzeTypeName(type.Name);
            if (!unilyzeByName.TryGetValue(unilyzeName, out var unilyzeType))
            {
                unexplained.Add($"{type.Name}: missing unilyze type '{unilyzeName}'");
                continue;
            }

            compared++;

            var unilyzeMethodSum = unilyzeType.Members
                .Where(m => string.Equals(m.MemberKind, "Method", StringComparison.Ordinal))
                .Sum(m => m.CyclomaticComplexity ?? 0);

            var delta = unilyzeMethodSum - type.OfficialCycCC;
            var constructs = SumConstructs(type.Methods);
            var memberBase = ComputeMemberBase(type);
            var explained = constructs.SwitchArms + constructs.Catches + constructs.Gotos
                - constructs.DefaultLabels - memberBase + constructs.BoolAmpOr;
            var residual = delta - explained;

            if (residual == 0)
                continue;

            var allowlistKey = type.ContainingType is null ? type.Name : $"{type.ContainingType}.{type.Name}";
            if (ResidualAllowlist.TryGetValue(allowlistKey, out var reason) && Math.Abs(residual) <= 1)
            {
                Console.Error.WriteLine(
                    $"allowlisted residual {type.Name}: delta={delta} explained={explained} residual={residual} ({reason})");
                continue;
            }

            unexplained.Add(
                $"{type.Name}: delta={delta} explained={explained} residual={residual} " +
                $"(switchArms={constructs.SwitchArms} catches={constructs.Catches} gotos={constructs.Gotos} " +
                $"defaultLabels={constructs.DefaultLabels} memberBase={memberBase} boolAmpOr={constructs.BoolAmpOr})");
        }

        if (unexplained.Count > 0)
        {
            Console.Error.WriteLine($"crossval compare: {unexplained.Count} type(s) with unexplained CycCC residual:");
            foreach (var line in unexplained)
                Console.Error.WriteLine($"  {line}");
            return 1;
        }

        Console.Error.WriteLine($"crossval compare: OK ({compared} types, residual identity holds)");
        return 0;
    }

    static ConstructTotals SumConstructs(IReadOnlyList<MethodConstructs> methods) => new(
        SwitchArms: methods.Sum(m => m.SwitchArms),
        Catches: methods.Sum(m => m.Catches),
        Gotos: methods.Sum(m => m.Gotos),
        DefaultLabels: methods.Sum(m => m.DefaultLabels),
        BoolAmpOr: methods.Sum(m => m.BoolAmpOr));

    static int ComputeMemberBase(TypeReport type)
    {
        var officialMethodSum = type.Members
            .Where(m => string.Equals(m.Kind, "Method", StringComparison.Ordinal) && IsUnilyzeAggregatedMethod(m.Name))
            .Sum(m => m.CycCC);
        return type.OfficialCycCC - officialMethodSum;
    }

    static bool IsUnilyzeAggregatedMethod(string metadataName) =>
        metadataName is not (".ctor" or ".cctor") && !metadataName.StartsWith("op_", StringComparison.Ordinal);

    static string ToUnilyzeTypeName(string metadataName) =>
        Regex.Replace(metadataName, "`(\\d+)", match =>
        {
            var arity = int.Parse(match.Groups[1].Value);
            return "<" + string.Join(',', Enumerable.Repeat("T", arity)) + ">";
        });

    readonly record struct ConstructTotals(
        int SwitchArms,
        int Catches,
        int Gotos,
        int DefaultLabels,
        int BoolAmpOr);
}

internal sealed record UnilyzeAnalysisDocument(IReadOnlyList<UnilyzeTypeNode> Types);

internal sealed record UnilyzeTypeNode(string Name, IReadOnlyList<UnilyzeMemberNode> Members);

internal sealed record UnilyzeMemberNode(string MemberKind, int? CyclomaticComplexity);
